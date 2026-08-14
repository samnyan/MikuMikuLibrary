using System.Text;
using MikuMikuLibrary.IO;
using MikuMikuLibrary.IO.Common;
using MikuMikuLibrary.IO.Sections;

namespace MikuMikuLibrary.Selection;

/// <summary>Little-endian FGO Arcade common selection properties.</summary>
public sealed class FgoSelCommonTable : BinaryFile
{
    public uint Version { get; private set; }
    public uint PropertyCount { get; private set; }
    public byte[] OriginalBytes { get; private set; } = Array.Empty<byte>();
    public List<FgoSelCommonProperty> Properties { get; } = new();

    public override BinaryFileFlags Flags => BinaryFileFlags.Load;
    public override Endianness Endianness => Endianness.Little;

    public override void Read(EndianBinaryReader reader, ISection section = null)
    {
        CaptureOriginalBytes(reader.BaseStream);
        Version = reader.ReadUInt32();
        PropertyCount = reader.ReadUInt32();

        if (PropertyCount > 1024 || 0x20L + PropertyCount * 16L > reader.Length)
            throw new InvalidDataException("Invalid FGO sel_common property table bounds");

        Properties.Clear();
        reader.BaseStream.Position = 0x20;
        for (var i = 0; i < PropertyCount; i++)
        {
            var descriptorEnd = reader.BaseStream.Position + 16;
            var valueSize = reader.ReadUInt32();
            var valueType = reader.ReadUInt32();
            var valueOffset = reader.ReadUInt32();
            var nameOffset = reader.ReadUInt32();

            var valueLength = checked((long)valueSize);
            if (valueOffset > (uint)reader.Length || valueLength > reader.Length - (long)valueOffset)
                throw new InvalidDataException($"Invalid sel_common value range for property {i}");

            Properties.Add(new FgoSelCommonProperty(
                i,
                ReadName(reader, nameOffset),
                valueSize,
                valueType,
                valueOffset,
                ReadBytes(reader, valueOffset, checked((int)valueLength))));

            reader.BaseStream.Position = descriptorEnd;
        }
    }

    public override void Write(EndianBinaryWriter writer, ISection section = null) =>
        throw new NotSupportedException("FGO sel_common.bin is read-only");

    private void CaptureOriginalBytes(Stream source)
    {
        long position = source.Position;
        source.Position = 0;
        using var copy = new MemoryStream();
        source.CopyTo(copy);
        OriginalBytes = copy.ToArray();
        source.Position = position;
    }

    private static string ReadName(EndianBinaryReader reader, uint offset)
    {
        if (offset > (uint)reader.Length || reader.Length - (long)offset < 4)
            return $"property_{offset:X}";

        reader.BaseStream.Position = offset;
        var length = reader.ReadUInt32();
        if (length > (ulong)(reader.Length - reader.Position))
            return $"property_{offset:X}";

        return Encoding.UTF8.GetString(reader.ReadBytes(checked((int)length)));
    }

    private static byte[] ReadBytes(EndianBinaryReader reader, uint offset, int length)
    {
        reader.BaseStream.Position = offset;
        var data = reader.ReadBytes(length);
        if (data.Length != length)
            throw new EndOfStreamException("Unexpected end of sel_common value data");

        return data;
    }
}

public sealed record FgoSelCommonProperty(
    int Index,
    string Name,
    uint ValueSize,
    uint ValueType,
    uint ValueOffset,
    byte[] Values);
