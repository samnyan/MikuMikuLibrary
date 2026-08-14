using System.IO.Compression;
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
/// The reader supports unencrypted raw, gzip, and chunked Zstandard entries found
/// in the FGO ROM. Chunked encrypted entries are indexed, but extraction is
/// rejected until the title-specific key/chunk format is supplied.
/// </remarks>
public sealed class FgoFarcArchive : BinaryFile, IArchive
{
    private const uint CompressionFlag = 0x02;
    private const uint EncryptionFlag = 0x04;
    private const uint ChunkedGzipFlag = 0x12;
    private const uint ChunkedZstdFlag = 0x30;
    private const uint MinimumHeaderSize = 0x20;

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
        _ = reader.ReadUInt32(); // inner header size
        BoundarySize = reader.ReadUInt32();
        EntryCount = reader.ReadUInt32();
        uint entrySize = reader.ReadUInt32();

        if (IndexSize < MinimumHeaderSize || IndexSize > reader.Length)
            throw new InvalidDataException($"Invalid FGO FArc index size: 0x{IndexSize:X}");

        if (entrySize < 0x10)
            throw new InvalidDataException($"Invalid FGO FArc entry size: 0x{entrySize:X}");

        if ((ArchiveFlags & EncryptionFlag) != 0)
            throw new NotSupportedException("FGO FArc archives with encrypted indexes are not supported");

        for (uint i = 0; i < EntryCount; i++)
        {
            string name = ReadIndexString(reader, IndexSize);
            uint offset = reader.ReadUInt32();
            uint storedSize = reader.ReadUInt32();
            uint uncompressedSize = reader.ReadUInt32();
            uint flags = reader.ReadUInt32();

            if (entrySize > 0x10)
                reader.SeekCurrent(entrySize - 0x10);

            if (string.IsNullOrEmpty(name))
                throw new InvalidDataException($"FGO FArc entry {i} has an empty name");

            if (offset > reader.Length || storedSize > reader.Length - offset)
                throw new InvalidDataException($"FGO FArc entry '{name}' points outside the archive");

            mEntries[name] = new Entry(name, offset, storedSize, uncompressedSize, flags);
        }

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

        if ((entry.Flags & EncryptionFlag) != 0)
            throw new NotSupportedException(
                $"FGO FArc entry '{fileName}' is encrypted; provide the title-specific chunk key before extracting it");

        var output = new MemoryStream(entry.UncompressedSize > 0 ? checked((int)entry.UncompressedSize) : 0);
        using (var source = new StreamView(mStream, mStream, entry.Offset, entry.StoredSize, true))
        {
            if ((entry.Flags & ChunkedZstdFlag) == ChunkedZstdFlag)
            {
                ExtractChunked(source, output, entry, useZstd: true);
            }
            else if ((entry.Flags & ChunkedGzipFlag) == ChunkedGzipFlag)
            {
                ExtractChunked(source, output, entry, useZstd: false);
            }
            else if ((entry.Flags & CompressionFlag) != 0)
            {
                using var gzip = new GZipStream(source, CompressionMode.Decompress, true);
                gzip.CopyTo(output);
            }
            else
            {
                source.CopyTo(output);
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

    private static string ReadIndexString(EndianBinaryReader reader, uint indexSize)
    {
        var bytes = new List<byte>();
        while (reader.Position < indexSize)
        {
            byte value = reader.ReadByte();
            if (value == 0)
                return Encoding.UTF8.GetString(bytes.ToArray());

            bytes.Add(value);
        }

        throw new InvalidDataException("Unterminated FGO FArc entry name");
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
