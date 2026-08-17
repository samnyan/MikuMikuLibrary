using System.Buffers.Binary;
using System.Text;
using MikuMikuLibrary.IO;
using MikuMikuLibrary.IO.Common;
using MikuMikuLibrary.IO.Sections;

namespace MikuMikuLibrary.Aets.Resources;

/// <summary>
/// A sprite table exported by the FGO Arcade client.
/// </summary>
/// <remarks>
/// The table is a compact little-endian record stream. It is unrelated to the
/// DIVA <c>SpriteSet</c> format, even though both formats describe atlas
/// rectangles. The final record in some shipped tables omits its optional
/// resource index, so the parser falls back to the record number in that case.
/// </remarks>
public sealed class FgoSpriteTable : BinaryFile
{
    private const int HeaderSize = 12;
    private const int RequiredFieldCount = 6;
    private const int OptionalFieldCount = 1;
    private const uint MaximumStringLength = 1 << 20;

    /// <summary>Gets the FGO Sprite table signature.</summary>
    public const uint Signature = 0x14090116;

    /// <inheritdoc />
    public override BinaryFileFlags Flags => BinaryFileFlags.Load;

    /// <inheritdoc />
    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>Gets the number of records declared by the table.</summary>
    public int RecordCount => Entries.Count;

    /// <summary>Gets all sprite entries in table order.</summary>
    public IReadOnlyList<FgoSpriteEntry> Entries { get; private set; }

    /// <summary>
    /// Gets the atlas names in the order used by <c>texture.bin</c>.
    /// FGO stores these names only in the Sprite table and sorts them by an
    /// ordinal resource-name comparison when building the texture set.
    /// </summary>
    public IReadOnlyList<string> TextureNames { get; private set; }

    /// <summary>Loads an FGO Sprite table from a stream.</summary>
    /// <param name="stream">A seekable stream containing the complete table.</param>
    /// <returns>The parsed Sprite table.</returns>
    public static FgoSpriteTable Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            throw new InvalidDataException("FGO Sprite tables require a seekable stream.");

        if (stream.Length > int.MaxValue)
            throw new InvalidDataException("FGO Sprite table is too large.");

        stream.Position = 0;
        var data = new byte[checked((int)stream.Length)];
        stream.ReadExactly(data);
        return Parse(data);
    }

    /// <summary>Loads an FGO Sprite table from a file.</summary>
    /// <param name="filePath">The table file path.</param>
    /// <returns>The parsed Sprite table.</returns>
    public static FgoSpriteTable ParseFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        return Parse(stream);
    }

    /// <inheritdoc />
    public override void Read(EndianBinaryReader reader, ISection section = null)
    {
        using var stream = new MemoryStream();
        reader.BaseStream.CopyTo(stream);
        var parsed = Parse(stream.ToArray());
        Entries = parsed.Entries;
        TextureNames = parsed.TextureNames;
    }

    /// <inheritdoc />
    public override void Write(EndianBinaryWriter writer, ISection section = null) =>
        throw new NotSupportedException("FGO Sprite table export is not implemented.");

    /// <summary>Determines whether a byte span starts with an FGO Sprite table.</summary>
    /// <param name="data">The bytes to inspect.</param>
    /// <returns><see langword="true"/> when the table signature is present.</returns>
    public static bool IsSpriteTable(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(data) == Signature;

    private static FgoSpriteTable Parse(byte[] data)
    {
        if (data.Length < HeaderSize || !IsSpriteTable(data))
            throw new InvalidDataException("Invalid FGO Sprite table signature.");

        int recordCount = checked((int)ReadUInt32(data, 4));
        if (recordCount > 1_000_000)
            throw new InvalidDataException("FGO Sprite table record count is unreasonable.");

        var entries = new List<FgoSpriteEntry>(recordCount);
        var textureNames = new List<string>();
        var textureNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int offset = HeaderSize;

        for (int index = 0; index < recordCount; index++)
        {
            string textureName = ReadString(data, ref offset);
            string spriteName = ReadString(data, ref offset);

            uint atlasWidth = ReadUInt32(data, ref offset);
            uint atlasHeight = ReadUInt32(data, ref offset);
            uint x0 = ReadUInt32(data, ref offset);
            uint y0 = ReadUInt32(data, ref offset);
            uint x1 = ReadUInt32(data, ref offset);
            uint y1 = ReadUInt32(data, ref offset);

            if (textureNameSet.Add(textureName))
            {
                textureNames.Add(textureName);
            }

            // All records before the last one carry the resource index. A few
            // tables omit the final value on their last record, so use the
            // stable record number as a fallback for that one case.
            uint resourceIndex = checked((uint)(index + 1));
            if (index != recordCount - 1 || data.Length - offset >= sizeof(uint))
                resourceIndex = ReadUInt32(data, ref offset);

            entries.Add(new FgoSpriteEntry(
                index,
                spriteName,
                textureName,
                0,
                atlasWidth,
                atlasHeight,
                x0,
                y0,
                x1,
                y1,
                resourceIndex));
        }

        if (offset != data.Length)
            throw new InvalidDataException(
                $"FGO Sprite table has {data.Length - offset} trailing bytes after {recordCount} records.");

        // texture.bin does not carry the source names. FGO's build pipeline
        // writes its texture set in resource-name order, while Sprite records
        // are emitted in UI/resource order. Numbering textures by first table
        // occurrence therefore points at the wrong atlas whenever those two
        // orders differ (for example gam_result_merge_bc7_013). Keep the
        // display names in the same order as texture.bin and remap every entry
        // to that stable order. IDA shows the game sorting the sprite resource
        // file names with an exact byte/string comparison, so use ordinal
        // ordering here rather than culture-sensitive or numeric sorting.
        var orderedTextureNames = textureNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var orderedTextureIndices = orderedTextureNames
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        var orderedEntries = entries.Select(entry => new FgoSpriteEntry(
            entry.Index,
            entry.Name,
            entry.TextureName,
            orderedTextureIndices[entry.TextureName],
            entry.AtlasWidth,
            entry.AtlasHeight,
            entry.X0,
            entry.Y0,
            entry.X1,
            entry.Y1,
            entry.ResourceIndex)).ToList();

        return new FgoSpriteTable(orderedEntries, orderedTextureNames);
    }

    private static uint ReadUInt32(byte[] data, ref int offset)
    {
        if (offset < 0 || data.Length - offset < sizeof(uint))
            throw new InvalidDataException("FGO Sprite table is truncated.");

        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));
        offset += sizeof(uint);
        return value;
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        if (offset < 0 || data.Length - offset < sizeof(uint))
            throw new InvalidDataException("FGO Sprite table is truncated.");

        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));
    }

    private static string ReadString(byte[] data, ref int offset)
    {
        uint length = ReadUInt32(data, ref offset);
        if (length > MaximumStringLength || length > data.Length - offset)
            throw new InvalidDataException("FGO Sprite table contains an invalid string length.");

        string value = Encoding.UTF8.GetString(data, offset, checked((int)length));
        offset += checked((int)length);
        return value;
    }

    public FgoSpriteTable()
    {
        Entries = Array.Empty<FgoSpriteEntry>();
        TextureNames = Array.Empty<string>();
    }

    private FgoSpriteTable(IReadOnlyList<FgoSpriteEntry> entries, IReadOnlyList<string> textureNames)
    {
        Entries = entries;
        TextureNames = textureNames;
    }
}

/// <summary>Describes one Sprite rectangle in an FGO texture atlas.</summary>
public sealed class FgoSpriteEntry
{
    /// <summary>Gets the zero-based record index.</summary>
    public int Index { get; }

    /// <summary>Gets the logical Sprite name.</summary>
    public string Name { get; }

    /// <summary>Gets the logical atlas name.</summary>
    public string TextureName { get; }

    /// <summary>Gets the zero-based texture index in the name-sorted <c>texture.bin</c>.</summary>
    public int TextureIndex { get; }

    /// <summary>Gets the atlas width recorded by the game.</summary>
    public uint AtlasWidth { get; }

    /// <summary>Gets the atlas height recorded by the game.</summary>
    public uint AtlasHeight { get; }

    /// <summary>Gets the first horizontal rectangle endpoint.</summary>
    public uint X0 { get; }

    /// <summary>Gets the first vertical rectangle endpoint.</summary>
    public uint Y0 { get; }

    /// <summary>Gets the second horizontal rectangle endpoint.</summary>
    public uint X1 { get; }

    /// <summary>Gets the second vertical rectangle endpoint.</summary>
    public uint Y1 { get; }

    /// <summary>Gets the resource index stored by the game.</summary>
    public uint ResourceIndex { get; }

    /// <summary>Creates one parsed Sprite entry.</summary>
    public FgoSpriteEntry(
        int index,
        string name,
        string textureName,
        int textureIndex,
        uint atlasWidth,
        uint atlasHeight,
        uint x0,
        uint y0,
        uint x1,
        uint y1,
        uint resourceIndex)
    {
        Index = index;
        Name = name;
        TextureName = textureName;
        TextureIndex = textureIndex;
        AtlasWidth = atlasWidth;
        AtlasHeight = atlasHeight;
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        ResourceIndex = resourceIndex;
    }
}
