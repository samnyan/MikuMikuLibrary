using MikuMikuLibrary.IO;
using MikuMikuLibrary.IO.Common;
using MikuMikuLibrary.IO.Sections;

namespace MikuMikuLibrary.Selection;

/// <summary>Little-endian FGO Arcade servant selection table.</summary>
public sealed class FgoSelSvtTable : BinaryFile
{
    public uint Version { get; private set; }
    public ulong LegacyRecordCount { get; private set; }
    public ulong LegacyRecordOffset { get; private set; }
    public ulong RecordCount { get; private set; }
    public ulong RecordOffset { get; private set; }
    public byte[] OriginalBytes { get; private set; } = Array.Empty<byte>();
    public List<FgoSelSvtRecord> Records { get; } = new();

    public override BinaryFileFlags Flags => BinaryFileFlags.Load;
    public override Endianness Endianness => Endianness.Little;

    public override void Read(EndianBinaryReader reader, ISection section = null)
    {
        CaptureOriginalBytes(reader.BaseStream);
        Version = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        LegacyRecordCount = reader.ReadUInt64();
        LegacyRecordOffset = reader.ReadUInt64();
        RecordCount = reader.ReadUInt64();
        RecordOffset = reader.ReadUInt64();

        if (RecordCount > int.MaxValue || RecordOffset > (ulong)reader.Length ||
            RecordCount > (ulong)(reader.Length - (long)RecordOffset) / FgoSelSvtRecord.Size)
            throw new InvalidDataException("Invalid FGO sel_svt record table bounds");

        Records.Clear();
        reader.BaseStream.Position = (long)RecordOffset;
        for (ulong i = 0; i < RecordCount; i++)
            Records.Add(new FgoSelSvtRecord(reader));
    }

    public override void Write(EndianBinaryWriter writer, ISection section = null) =>
        throw new NotSupportedException("FGO sel_svt.bin is read-only");

    private void CaptureOriginalBytes(Stream source)
    {
        long position = source.Position;
        source.Position = 0;
        using var copy = new MemoryStream();
        source.CopyTo(copy);
        OriginalBytes = copy.ToArray();
        source.Position = position;
    }
}

/// <summary>A raw 80-byte servant selection record.</summary>
public sealed class FgoSelSvtRecord
{
    public const int Size = 80;

    /// <summary>The 64-bit composite key used by the game for binary-search lookup.</summary>
    public ulong Key { get; }

    /// <summary>The low 32-bit ID component of <see cref="Key" />.</summary>
    public uint Id => (uint)Key;

    public uint KeyHigh => (uint)(Key >> 32);
    public uint[] Fields { get; } = new uint[18];

    public FgoSelSvtRecord(EndianBinaryReader reader)
    {
        Key = reader.ReadUInt64();
        for (var i = 0; i < Fields.Length; i++)
            Fields[i] = reader.ReadUInt32();
    }
}
