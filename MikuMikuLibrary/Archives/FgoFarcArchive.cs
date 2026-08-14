using System.IO.Compression;
using System.Security.Cryptography;
using MikuMikuLibrary.IO;
using MikuMikuLibrary.IO.Common;
using MikuMikuLibrary.IO.Sections;
using ZstdSharp;

namespace MikuMikuLibrary.Archives;

/// <summary>
/// Read-only reader for the FArc v3 container used by FATE/Grand Order Arcade.
/// </summary>
/// <remarks>
/// FGO uses the <c>FARc</c> signature. This is a different on-disk variant from
/// DIVA's <c>FArC</c> archive handled by <see cref="FarcArchive"/>. Integers in
/// this variant are big-endian, and each entry carries its own compression flags.
/// The reader supports raw, gzip, and chunked Zstandard entries found in the
/// FGO ROM. Archive indexes and entry payloads use the same title key and
/// AES-CBC layout when their encryption flag is set.
/// </remarks>
public sealed class FgoFarcArchive : BinaryFile, IArchive
{
    private const uint CompressionFlag = 0x02;
    private const uint EncryptionFlag = 0x04;
    private const uint ChunkedGzipFlag = 0x12;
    private const uint ChunkedZstdFlag = 0x30;
    private const uint MinimumHeaderSize = 0x20;
    // FGO Arcade initializes the common FARc key from the ASCII hex string
    // 62EC7CD79141695E53592ACC10CDC04C during startup (see ago.exe).
    private static readonly byte[] FgoArchiveKey = Convert.FromHexString(
        "62EC7CD79141695E53592ACC10CDC04C");

    private readonly Dictionary<string, Entry> mEntries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the size of the header and entry index in bytes.</summary>
    public uint IndexSize { get; private set; }

    /// <summary>Gets the archive-level flags.</summary>
    public uint ArchiveFlags { get; private set; }

    /// <summary>Gets the alignment value stored in the archive header.</summary>
    public uint BoundarySize { get; private set; }

    /// <summary>Gets the number of entries declared by the archive.</summary>
    public uint EntryCount { get; private set; }

    /// <inheritdoc />
    public override BinaryFileFlags Flags => BinaryFileFlags.Load | BinaryFileFlags.UsesSourceStream;

    /// <inheritdoc />
    public override Endianness Endianness => Endianness.Big;

    /// <inheritdoc />
    public bool CanAdd => false;

    /// <inheritdoc />
    public bool CanRemove => false;

    /// <inheritdoc />
    public IEnumerable<string> FileNames => mEntries.Keys;

    /// <inheritdoc />
    public bool Contains(string fileName) => mEntries.ContainsKey(fileName);

    /// <summary>Gets metadata for all entries in archive order.</summary>
    public IReadOnlyList<FgoFarcEntryInfo> Entries => mEntries.Values
        .Select(entry => new FgoFarcEntryInfo(
            entry.Name,
            entry.Offset,
            entry.StoredSize,
            entry.UncompressedSize,
            entry.Flags))
        .ToArray();

    /// <inheritdoc />
    public override void Read(EndianBinaryReader reader, ISection section = null)
    {
        mEntries.Clear();

        string signature = reader.ReadString(StringBinaryFormat.FixedLength, 4);
        if (signature != "FARc")
            throw new InvalidDataException("Invalid FGO FArc signature (expected FARc)");

        IndexSize = reader.ReadUInt32();
        ArchiveFlags = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // checksum

        if (IndexSize < MinimumHeaderSize || IndexSize > reader.Length)
            throw new InvalidDataException($"Invalid FGO FArc index size: 0x{IndexSize:X}");

        if ((ArchiveFlags & EncryptionFlag) != 0)
        {
            // For encrypted v3 archives IndexSize includes the eight-byte
            // signature/size prefix. The encrypted payload starts at offset
            // 0x10, consists of a 16-byte IV followed by AES-CBC ciphertext,
            // and contains the remaining index header plus entry records.
            int encryptedSize = checked((int)IndexSize - 8);
            if (encryptedSize < 0x20 || reader.Position > reader.Length - encryptedSize)
                throw new InvalidDataException("Encrypted FGO FArc index is truncated");

            byte[] encryptedIndex = reader.ReadBytes(encryptedSize);
            byte[] decryptedIndex = DecryptIndex(encryptedIndex);
            using var indexReader = new EndianBinaryReader(
                new MemoryStream(decryptedIndex, writable: false),
                reader.Encoding,
                Endianness.Big,
                leaveOpen: false);

            _ = indexReader.ReadUInt32(); // inner header size
            BoundarySize = indexReader.ReadUInt32();
            EntryCount = indexReader.ReadUInt32();
            uint encryptedEntrySize = indexReader.ReadUInt32();
            ReadEntries(indexReader, encryptedEntrySize, reader.Length);
            return;
        }

        _ = reader.ReadUInt32(); // inner header size
        BoundarySize = reader.ReadUInt32();
        EntryCount = reader.ReadUInt32();
        uint entrySize = reader.ReadUInt32();
        ReadEntries(reader, entrySize, reader.Length);

        // The v3 LIMIT/IndexSize value is the loop boundary used by the game,
        // not necessarily the byte immediately after the final variable-length
        // entry. The entry count and each entry's bounds are authoritative here.
    }

    /// <inheritdoc />
    public override void Write(EndianBinaryWriter writer, ISection section = null) =>
        throw new NotSupportedException("FGO Arcade FArc archives are read-only");

    /// <inheritdoc />
    public EntryStream Open(string fileName, EntryStreamMode mode)
    {
        if (!mEntries.TryGetValue(fileName, out var entry))
            throw new KeyNotFoundException($"FGO FArc entry not found: {fileName}");

        var output = new MemoryStream(entry.UncompressedSize > 0 ? checked((int)entry.UncompressedSize) : 0);
        using (var source = new StreamView(mStream, mStream, entry.Offset, entry.StoredSize, true))
        {
            if ((entry.Flags & EncryptionFlag) != 0)
            {
                using var encrypted = new MemoryStream();
                source.CopyTo(encrypted);
                using var decrypted = new MemoryStream(
                    DecryptAesPayload(encrypted.ToArray()), writable: false);
                ExtractEntryPayload(decrypted, output, entry);
            }
            else
            {
                ExtractEntryPayload(source, output, entry);
            }
        }

        output.Position = 0;
        return new EntryStream(entry.Name, output);
    }

    private static void ExtractChunked(Stream source, Stream destination, Entry entry, bool useZstd)
    {
        using var compressed = new MemoryStream(checked((int)entry.StoredSize));
        source.CopyTo(compressed);
        byte[] data = compressed.ToArray();
        if (data.Length < 8)
            throw new InvalidDataException($"FGO FArc chunk table for '{entry.Name}' is truncated");

        int cursor = 4; // block size; the game uses it as the nominal output chunk size
        var chunkSizes = new List<int>();
        while (cursor + 4 <= data.Length)
        {
            uint size = ReadUInt32LittleEndian(data, cursor);
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
                // The frame carries its own content size. The entry size is a safe
                // upper bound for frames whose content size is omitted.
                int maxSize = checked((int)Math.Max(entry.UncompressedSize, 1));
                destination.Write(decompressor!.Unwrap(chunk, maxSize));
            }
            else
            {
                using var chunkStream = new MemoryStream(chunk, writable: false);
                using var gzip = new GZipStream(chunkStream, CompressionMode.Decompress);
                gzip.CopyTo(destination);
            }
        }
    }

    private static uint ReadUInt32LittleEndian(byte[] data, int offset) =>
        (uint)(data[offset] |
               (data[offset + 1] << 8) |
               (data[offset + 2] << 16) |
               (data[offset + 3] << 24));

    /// <inheritdoc />
    public void Add(string fileName, Stream source, bool leaveOpen, ConflictPolicy conflictPolicy) =>
        throw new NotSupportedException("FGO FArc archives are read-only");

    /// <inheritdoc />
    public void Add(string fileName, string sourceFilePath, ConflictPolicy conflictPolicy) =>
        throw new NotSupportedException("FGO FArc archives are read-only");

    /// <inheritdoc />
    public void Remove(string fileName) =>
        throw new NotSupportedException("FGO FArc archives are read-only");

    /// <inheritdoc />
    public void Clear() =>
        throw new NotSupportedException("FGO FArc archives are read-only");

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator() => mEntries.Keys.GetEnumerator();

    /// <inheritdoc />
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

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
        if (entrySize < 0x10)
            throw new InvalidDataException($"Invalid FGO FArc entry size: 0x{entrySize:X}");

        for (uint i = 0; i < EntryCount; i++)
        {
            // IndexSize is the game's LIMIT value. In v3 archives it can end
            // before the final variable-length name/metadata record (the
            // sel_global_properties archive is eight bytes past this value),
            // so the entry count and stream bounds are the authoritative limits.
            string name = ReadIndexString(reader);
            uint offset = reader.ReadUInt32();
            uint storedSize = reader.ReadUInt32();
            uint uncompressedSize = reader.ReadUInt32();
            uint flags = reader.ReadUInt32();

            if (entrySize > 0x10)
                reader.SeekCurrent(entrySize - 0x10);

            if (string.IsNullOrEmpty(name))
                throw new InvalidDataException($"FGO FArc entry {i} has an empty name");

            if (offset > archiveLength || storedSize > archiveLength - offset)
                throw new InvalidDataException($"FGO FArc entry '{name}' points outside the archive");

            mEntries[name] = new Entry(name, offset, storedSize, uncompressedSize, flags);
        }
    }

    private static byte[] DecryptIndex(byte[] encryptedIndex)
        => DecryptAesPayload(encryptedIndex);

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

    private static void ExtractEntryPayload(Stream source, Stream destination, Entry entry)
    {
        if ((entry.Flags & ChunkedZstdFlag) == ChunkedZstdFlag)
        {
            ExtractChunked(source, destination, entry, useZstd: true);
        }
        else if ((entry.Flags & ChunkedGzipFlag) == ChunkedGzipFlag)
        {
            ExtractChunked(source, destination, entry, useZstd: false);
        }
        else if ((entry.Flags & CompressionFlag) != 0)
        {
            using var gzip = new GZipStream(source, CompressionMode.Decompress, true);
            gzip.CopyTo(destination);
        }
        else
        {
            source.CopyTo(destination);
        }
    }

    private sealed record Entry(string Name, uint Offset, uint StoredSize, uint UncompressedSize, uint Flags);
}

/// <summary>Metadata for one FGO FArc entry.</summary>
public sealed record FgoFarcEntryInfo(
    string Name,
    uint Offset,
    uint StoredSize,
    uint UncompressedSize,
    uint Flags);
