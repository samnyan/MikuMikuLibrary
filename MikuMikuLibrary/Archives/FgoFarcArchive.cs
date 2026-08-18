using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using MikuMikuLibrary.IO;
using MikuMikuLibrary.IO.Common;
using MikuMikuLibrary.IO.Sections;
using ZstdSharp;

namespace MikuMikuLibrary.Archives;

/// <summary>Reader and writer for FATE/Grand Order Arcade's FArc v3 container.</summary>
/// <remarks>
/// FGO uses the <c>FARc</c> signature. Integers in the index are big-endian,
/// while chunk-size tables are little-endian. Entry flags select raw, gzip,
/// chunked gzip, or chunked Zstandard payloads. The game writer stores zero in
/// the archive checksum field; this writer follows that convention.
/// </remarks>
public sealed class FgoFarcArchive : BinaryFile, IArchive
{
    private const uint CompressionFlag = 0x02;
    private const uint EncryptionFlag = 0x04;
    private const uint ChunkedGzipFlag = 0x12;
    private const uint ChunkedZstdFlag = 0x30;
    private const uint MinimumHeaderSize = 0x18;
    private const uint DefaultBoundarySize = 1;
    private const uint DefaultInnerHeaderSize = 0x10;
    private const uint DefaultEntrySize = 0x10;
    private const int DefaultChunkSize = 0x400;

    // FGO Arcade initializes this key from the ASCII hex string in ago.exe.
    private static readonly byte[] FgoArchiveKey = Convert.FromHexString(
        "62EC7CD79141695E53592ACC10CDC04C");

    private readonly Dictionary<string, Entry> mEntries = new(StringComparer.OrdinalIgnoreCase);
    private uint mInnerHeaderSize = DefaultInnerHeaderSize;
    private uint mEntrySize = DefaultEntrySize;

    public uint IndexSize { get; private set; }
    public uint ArchiveFlags { get; private set; } = 0x40;
    public uint BoundarySize { get; private set; } = DefaultBoundarySize;
    public uint EntryCount { get; private set; }

    public override BinaryFileFlags Flags =>
        BinaryFileFlags.Load | BinaryFileFlags.Save | BinaryFileFlags.UsesSourceStream;
    public override Endianness Endianness => Endianness.Big;
    public bool CanAdd => true;
    public bool CanRemove => true;
    public IEnumerable<string> FileNames => mEntries.Keys;
    public bool Contains(string fileName) => mEntries.ContainsKey(fileName);

    public IReadOnlyList<FgoFarcEntryInfo> Entries => mEntries.Values
        .Select(entry => new FgoFarcEntryInfo(entry.Name, entry.Offset, entry.StoredSize,
            entry.UncompressedSize, entry.Flags)).ToArray();

    public override void Read(EndianBinaryReader reader, ISection section = null)
    {
        Clear();
        string signature = reader.ReadString(StringBinaryFormat.FixedLength, 4);
        if (signature != "FARc")
            throw new InvalidDataException("Invalid FGO FArc signature (expected FARc)");

        IndexSize = reader.ReadUInt32();
        ArchiveFlags = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // The game writer currently stores checksum zero.
        if (IndexSize < MinimumHeaderSize || IndexSize > reader.Length)
            throw new InvalidDataException($"Invalid FGO FArc index size: 0x{IndexSize:X}");

        if ((ArchiveFlags & EncryptionFlag) != 0)
        {
            int encryptedSize = checked((int)IndexSize - 8);
            if (encryptedSize < 0x20 || reader.Position > reader.Length - encryptedSize)
                throw new InvalidDataException("Encrypted FGO FArc index is truncated");
            byte[] decryptedIndex = DecryptAesPayload(reader.ReadBytes(encryptedSize));
            using var indexReader = new EndianBinaryReader(new MemoryStream(decryptedIndex, false),
                reader.Encoding, Endianness.Big, leaveOpen: false);
            mInnerHeaderSize = indexReader.ReadUInt32();
            BoundarySize = indexReader.ReadUInt32();
            EntryCount = indexReader.ReadUInt32();
            mEntrySize = indexReader.ReadUInt32();
            ReadEntries(indexReader, mEntrySize, reader.Length);
            return;
        }

        mInnerHeaderSize = reader.ReadUInt32();
        BoundarySize = reader.ReadUInt32();
        EntryCount = reader.ReadUInt32();
        mEntrySize = reader.ReadUInt32();
        ReadEntries(reader, mEntrySize, reader.Length);
    }

    public override void Write(EndianBinaryWriter writer, ISection section = null)
    {
        uint boundary = BoundarySize == 0 ? DefaultBoundarySize : BoundarySize;
        Entry[] entries = mEntries.Values.ToArray();
        var payloads = new List<byte[]>(entries.Length);

        foreach (Entry entry in entries)
        {
            byte[] payload;
            if (entry.Stream != null)
            {
                byte[] uncompressed = ReadAll(entry.Stream);
                entry.UncompressedSize = checked((uint)uncompressed.Length);
                payload = EncodePayload(uncompressed, entry.Flags, entry.ChunkSize);
                if ((entry.Flags & EncryptionFlag) != 0)
                    payload = EncryptAesPayload(payload);
            }
            else
                payload = ReadRawPayload(entry);
            payloads.Add(payload);
        }

        uint innerHeaderSize = Math.Max(mInnerHeaderSize, DefaultInnerHeaderSize);
        uint entrySize = Math.Max(mEntrySize, DefaultEntrySize);
        long dataOffset = Align(IndexSize + 8L, boundary);
        for (int i = 0; i < entries.Length; i++)
        {
            dataOffset = Align(dataOffset, boundary);
            entries[i].Offset = checked((uint)dataOffset);
            entries[i].StoredSize = checked((uint)payloads[i].Length);
            dataOffset += payloads[i].Length;
        }

        byte[] plainIndex = BuildPlainIndex(innerHeaderSize, entrySize, boundary, entries);
        byte[] indexPayload = (ArchiveFlags & EncryptionFlag) != 0
            ? EncryptAesPayload(plainIndex) : plainIndex;
        IndexSize = checked((uint)(8 + indexPayload.Length));

        // IndexSize affects the data start. Recalculate offsets once with the
        // final encrypted/plain index length, then serialize the index again.
        dataOffset = Align(IndexSize + 8L, boundary);
        for (int i = 0; i < entries.Length; i++)
        {
            dataOffset = Align(dataOffset, boundary);
            entries[i].Offset = checked((uint)dataOffset);
            dataOffset += payloads[i].Length;
        }
        plainIndex = BuildPlainIndex(innerHeaderSize, entrySize, boundary, entries);
        indexPayload = (ArchiveFlags & EncryptionFlag) != 0
            ? EncryptAesPayload(plainIndex) : plainIndex;
        IndexSize = checked((uint)(8 + indexPayload.Length));

        writer.Write(Encoding.ASCII.GetBytes("FARc"));
        WriteUInt32BigEndian(writer, IndexSize);
        WriteUInt32BigEndian(writer, ArchiveFlags);
        WriteUInt32BigEndian(writer, 0);
        writer.Write(indexPayload);
        WriteZeros(writer, checked((int)(Align(writer.Position, boundary) - writer.Position)));

        for (int i = 0; i < entries.Length; i++)
        {
            WriteZeros(writer, checked((int)(entries[i].Offset - writer.Position)));
            writer.Write(payloads[i]);
            if (entries[i].Stream != null)
            {
                if (entries[i].OwnsStream)
                    entries[i].Stream.Dispose();
                entries[i].Stream = null;
                entries[i].OwnsStream = false;
            }
        }
        EntryCount = checked((uint)entries.Length);
    }

    public EntryStream Open(string fileName, EntryStreamMode mode)
    {
        if (!mEntries.TryGetValue(fileName, out Entry entry))
            throw new KeyNotFoundException($"FGO FArc entry not found: {fileName}");
        if (entry.Stream != null)
        {
            entry.Stream.Position = 0;
            var copy = new MemoryStream();
            entry.Stream.CopyTo(copy);
            copy.Position = 0;
            return new EntryStream(entry.Name, copy);
        }

        var output = new MemoryStream(entry.UncompressedSize > 0 ? checked((int)entry.UncompressedSize) : 0);
        using (var source = new StreamView(mStream, mStream, entry.Offset, entry.StoredSize, true))
        {
            if ((entry.Flags & EncryptionFlag) != 0)
            {
                using var encrypted = new MemoryStream();
                source.CopyTo(encrypted);
                using var decrypted = new MemoryStream(DecryptAesPayload(encrypted.ToArray()), false);
                ExtractEntryPayload(decrypted, output, entry);
            }
            else
                ExtractEntryPayload(source, output, entry);
        }
        output.Position = 0;
        return new EntryStream(entry.Name, output);
    }

    public void Add(string fileName, Stream source, bool leaveOpen, ConflictPolicy conflictPolicy)
    {
        if (mEntries.TryGetValue(fileName, out Entry entry))
        {
            switch (conflictPolicy)
            {
                case ConflictPolicy.RaiseError:
                    throw new InvalidOperationException($"Entry already exists ({fileName})");
                case ConflictPolicy.Ignore:
                    return;
                case ConflictPolicy.Replace:
                    entry.Dispose();
                    entry.Stream = source;
                    entry.OwnsStream = !leaveOpen;
                    return;
            }
        }
        mEntries.Add(fileName, new Entry { Name = fileName, Stream = source, OwnsStream = !leaveOpen });
        EntryCount = checked((uint)mEntries.Count);
    }

    public void Add(string fileName, string sourceFilePath, ConflictPolicy conflictPolicy) =>
        Add(fileName, File.OpenRead(sourceFilePath), false, conflictPolicy);

    public void Remove(string fileName)
    {
        if (mEntries.Remove(fileName, out Entry entry))
            entry.Dispose();
        EntryCount = checked((uint)mEntries.Count);
    }

    public void Clear()
    {
        foreach (Entry entry in mEntries.Values)
            entry.Dispose();
        mEntries.Clear();
        EntryCount = 0;
    }

    public IEnumerator<string> GetEnumerator() => mEntries.Keys.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private byte[] BuildPlainIndex(uint innerHeaderSize, uint entrySize, uint boundary, Entry[] entries)
    {
        using var stream = new MemoryStream();
        using var writer = new EndianBinaryWriter(stream, Encoding.UTF8, Endianness.Big, leaveOpen: true);
        WriteUInt32BigEndian(writer, innerHeaderSize);
        WriteUInt32BigEndian(writer, boundary);
        WriteUInt32BigEndian(writer, checked((uint)entries.Length));
        WriteUInt32BigEndian(writer, entrySize);
        WriteZeros(writer, checked((int)(innerHeaderSize - DefaultInnerHeaderSize)));
        foreach (Entry entry in entries)
        {
            writer.Write(Encoding.UTF8.GetBytes(entry.Name));
            writer.Write((byte)0);
            WriteUInt32BigEndian(writer, entry.Offset);
            WriteUInt32BigEndian(writer, entry.StoredSize);
            WriteUInt32BigEndian(writer, entry.UncompressedSize);
            WriteUInt32BigEndian(writer, entry.Flags);
            WriteZeros(writer, checked((int)(entrySize - DefaultEntrySize)));
        }
        return stream.ToArray();
    }

    private byte[] ReadRawPayload(Entry entry)
    {
        if (mStream == null || entry.StoredSize == 0)
            return Array.Empty<byte>();
        mStream.Seek(entry.Offset, SeekOrigin.Begin);
        byte[] payload = new byte[entry.StoredSize];
        mStream.ReadExactly(payload);
        return payload;
    }

    private uint ReadChunkSize(Entry entry)
    {
        bool isChunked = (entry.Flags & ChunkedZstdFlag) == ChunkedZstdFlag ||
                         (entry.Flags & ChunkedGzipFlag) == ChunkedGzipFlag;
        if (!isChunked ||
            mStream == null || entry.StoredSize < 4)
            return DefaultChunkSize;

        long position = mStream.Position;
        try
        {
            byte[] header;
            if ((entry.Flags & EncryptionFlag) != 0)
            {
                byte[] encrypted = ReadRawPayload(entry);
                header = DecryptAesPayload(encrypted);
            }
            else
            {
                mStream.Seek(entry.Offset, SeekOrigin.Begin);
                header = new byte[4];
                mStream.ReadExactly(header);
            }

            uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
            return chunkSize == 0 ? DefaultChunkSize : chunkSize;
        }
        finally
        {
            mStream.Position = position;
        }
    }

    private static byte[] EncodePayload(byte[] data, uint flags, uint chunkSize)
    {
        if ((flags & ChunkedZstdFlag) == ChunkedZstdFlag)
            return EncodeChunked(data, true, chunkSize);
        if ((flags & ChunkedGzipFlag) == ChunkedGzipFlag)
            return EncodeChunked(data, false, chunkSize);
        if ((flags & CompressionFlag) != 0)
            return EncodeGzip(data);
        return data;
    }

    private static byte[] EncodeChunked(byte[] data, bool useZstd, uint chunkSize)
    {
        int blockSize = checked((int)(chunkSize == 0 ? DefaultChunkSize : chunkSize));
        var chunks = new List<byte[]>();
        using var compressor = useZstd ? new Compressor() : null;
        for (int offset = 0; offset < data.Length || (data.Length == 0 && offset == 0); offset += blockSize)
        {
            int length = Math.Min(blockSize, data.Length - offset);
            byte[] source = data.AsSpan(offset, length).ToArray();
            chunks.Add(useZstd ? compressor!.Wrap(source).ToArray() : EncodeGzip(source));
            if (data.Length == 0)
                break;
        }
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(blockSize);
        foreach (byte[] chunk in chunks)
            writer.Write(chunk.Length);
        foreach (byte[] chunk in chunks)
            writer.Write(chunk);
        return output.ToArray();
    }

    private static byte[] EncodeGzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
            gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] EncryptAesPayload(byte[] plaintext)
    {
        int paddedLength = checked((int)Align(Math.Max(plaintext.Length, 1), 16));
        byte[] padded = new byte[paddedLength];
        Buffer.BlockCopy(plaintext, 0, padded, 0, plaintext.Length);
        byte[] iv = new byte[16];
        RandomNumberGenerator.Fill(iv);
        using var aes = Aes.Create();
        aes.Key = FgoArchiveKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();
        return iv.Concat(encryptor.TransformFinalBlock(padded, 0, padded.Length)).ToArray();
    }

    private static byte[] ReadAll(Stream source)
    {
        if (source.CanSeek)
            source.Position = 0;
        using var copy = new MemoryStream();
        source.CopyTo(copy);
        return copy.ToArray();
    }

    private static long Align(long value, uint alignment)
    {
        if (alignment <= 1)
            return value;
        long remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    private static void WriteUInt32BigEndian(EndianBinaryWriter writer, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        writer.Write(bytes.ToArray());
    }

    private static void WriteZeros(EndianBinaryWriter writer, int count)
    {
        if (count > 0)
            writer.Write(new byte[count]);
    }

    private static string ReadIndexString(EndianBinaryReader reader)
    {
        var bytes = new List<byte>();
        while (reader.Position < reader.Length)
        {
            byte value = reader.ReadByte();
            if (value == 0)
                return Encoding.UTF8.GetString(bytes.ToArray());
            bytes.Add(value);
        }
        throw new InvalidDataException("Unterminated FGO FArc entry name");
    }

    private void ReadEntries(EndianBinaryReader reader, uint entrySize, long archiveLength)
    {
        if (entrySize < DefaultEntrySize)
            throw new InvalidDataException($"Invalid FGO FArc entry size: 0x{entrySize:X}");
        for (uint i = 0; i < EntryCount; i++)
        {
            string name = ReadIndexString(reader);
            uint offset = reader.ReadUInt32();
            uint storedSize = reader.ReadUInt32();
            uint uncompressedSize = reader.ReadUInt32();
            uint flags = reader.ReadUInt32();
            if (entrySize > DefaultEntrySize)
                reader.SeekCurrent(entrySize - DefaultEntrySize);
            if (string.IsNullOrEmpty(name))
                throw new InvalidDataException($"FGO FArc entry {i} has an empty name");
            if (offset > archiveLength || storedSize > archiveLength - offset)
                throw new InvalidDataException($"FGO FArc entry '{name}' points outside the archive");
            var entry = new Entry(name, offset, storedSize, uncompressedSize, flags);
            entry.ChunkSize = ReadChunkSize(entry);
            mEntries[name] = entry;
        }
    }

    private static byte[] DecryptAesPayload(byte[] encryptedPayload)
    {
        if (encryptedPayload.Length < 0x20 || (encryptedPayload.Length - 0x10) % 16 != 0)
            throw new InvalidDataException("Encrypted FGO FArc index has an invalid AES block length");
        byte[] iv = encryptedPayload.AsSpan(0, 16).ToArray();
        using var aes = Aes.Create();
        aes.Key = FgoArchiveKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encryptedPayload, 16, encryptedPayload.Length - 16);
    }

    private static void ExtractChunked(Stream source, Stream destination, Entry entry, bool useZstd)
    {
        using var compressed = new MemoryStream(checked((int)entry.StoredSize));
        source.CopyTo(compressed);
        byte[] data = compressed.ToArray();
        if (data.Length < 8)
            throw new InvalidDataException($"FGO FArc chunk table for '{entry.Name}' is truncated");
        int cursor = 4;
        var chunkSizes = new List<int>();
        while (cursor + 4 <= data.Length)
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor, 4));
            if (size == 0x08088B1F || size == 0xFD2FB528)
                break;
            if (size == 0 || size > int.MaxValue)
                throw new InvalidDataException($"Invalid FGO FArc chunk size 0x{size:X}");
            chunkSizes.Add((int)size);
            cursor += 4;
        }
        if (chunkSizes.Count == 0)
            throw new InvalidDataException($"FGO FArc chunk table for '{entry.Name}' has no chunks");
        int dataOffset = cursor;
        long totalCompressedSize = chunkSizes.Sum(size => (long)size);
        if (totalCompressedSize > data.Length - dataOffset)
            throw new InvalidDataException($"FGO FArc chunk data for '{entry.Name}' is truncated");
        using var decompressor = useZstd ? new Decompressor() : null;
        foreach (int chunkSize in chunkSizes)
        {
            byte[] chunk = data.AsSpan(dataOffset, chunkSize).ToArray();
            dataOffset += chunkSize;
            if (useZstd)
            {
                int maxSize = checked((int)Math.Max(entry.UncompressedSize, 1));
                destination.Write(decompressor!.Unwrap(chunk, maxSize));
            }
            else
            {
                using var chunkStream = new MemoryStream(chunk, false);
                using var gzip = new GZipStream(chunkStream, CompressionMode.Decompress);
                gzip.CopyTo(destination);
            }
        }
    }

    private static void ExtractEntryPayload(Stream source, Stream destination, Entry entry)
    {
        if ((entry.Flags & ChunkedZstdFlag) == ChunkedZstdFlag)
            ExtractChunked(source, destination, entry, true);
        else if ((entry.Flags & ChunkedGzipFlag) == ChunkedGzipFlag)
            ExtractChunked(source, destination, entry, false);
        else if ((entry.Flags & CompressionFlag) != 0)
        {
            using var gzip = new GZipStream(source, CompressionMode.Decompress, true);
            gzip.CopyTo(destination);
        }
        else
        {
            // AES-CBC has no padding marker in this format. Trim the zero
            // padding from encrypted raw entries using the index size field.
            long remaining = entry.UncompressedSize;
            if (remaining == 0)
            {
                source.CopyTo(destination);
                return;
            }

            byte[] buffer = new byte[81920];
            while (remaining > 0)
            {
                int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                    break;
                destination.Write(buffer, 0, read);
                remaining -= read;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (Entry entry in mEntries.Values)
                entry.Dispose();
        base.Dispose(disposing);
    }

    private sealed class Entry : IDisposable
    {
        public string Name;
        public uint Offset;
        public uint StoredSize;
        public uint UncompressedSize;
        public uint Flags;
        public uint ChunkSize;
        public Stream Stream;
        public bool OwnsStream;

        public Entry() { }
        public Entry(string name, uint offset, uint storedSize, uint uncompressedSize, uint flags)
        {
            Name = name;
            Offset = offset;
            StoredSize = storedSize;
            UncompressedSize = uncompressedSize;
            Flags = flags;
        }
        public void Dispose()
        {
            if (OwnsStream)
                Stream?.Dispose();
            Stream = null;
            OwnsStream = false;
        }
    }
}

public sealed record FgoFarcEntryInfo(
    string Name,
    uint Offset,
    uint StoredSize,
    uint UncompressedSize,
    uint Flags);
