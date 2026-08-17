using System.Buffers.Binary;
using System.Text;
using MikuMikuLibrary.IO;
using MikuMikuLibrary.IO.Common;
using MikuMikuLibrary.IO.Sections;

namespace MikuMikuLibrary.Aets;

/// <summary>
/// The AET export used by FGO Arcade.  It is not compatible with the DIVA
/// AETC/classic AET format: the file contains a relative record table whose
/// entries describe compositions, assets and layers.
/// </summary>
public sealed class FgoAetSet : BinaryFile
{
    public const uint Magic = 0x41450103;

    public override BinaryFileFlags Flags => BinaryFileFlags.Load;
    public override Encoding Encoding => Encoding.UTF8;

    public bool IsBigEndian { get; private set; }
    public uint DeclaredSize { get; private set; }
    public uint HeaderOffset0 { get; private set; }
    public uint HeaderOffset1 { get; private set; }
    public uint HeaderOffset2 { get; private set; }
    public uint HeaderOffset3 { get; private set; }
    public List<FgoAetRecord> Records { get; } = new();

    public override void Read(EndianBinaryReader reader, ISection section = null)
    {
        Records.Clear();
        var stream = reader.BaseStream;
        if (!stream.CanSeek)
            throw new InvalidDataException("FGO AET requires a seekable stream.");

        stream.Position = 0;
        if (stream.Length > int.MaxValue)
            throw new InvalidDataException("FGO AET is too large.");

        var bytes = new byte[checked((int)stream.Length)];
        int read = 0;
        while (read < bytes.Length)
        {
            int chunk = stream.Read(bytes, read, bytes.Length - read);
            if (chunk == 0)
                throw new EndOfStreamException();
            read += chunk;
        }

        var data = new Reader(bytes);
        uint littleMagic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
        if (littleMagic == Magic)
            IsBigEndian = false;
        else if (littleMagic == 0x03014541)
            IsBigEndian = true;
        else
            throw new InvalidDataException("Invalid FGO AET signature.");

        data.BigEndian = IsBigEndian;
        DeclaredSize = data.U32(4);
        if (DeclaredSize != bytes.Length)
            throw new InvalidDataException($"FGO AET size field is {DeclaredSize}, actual size is {bytes.Length}.");

        HeaderOffset0 = data.U32(8);
        HeaderOffset1 = data.U32(12);
        HeaderOffset2 = data.U32(16);
        HeaderOffset3 = data.U32(20);
        data.CheckRange(0, 36);

        uint countValue = data.U32(32);
        if (countValue > 100000)
            throw new InvalidDataException("FGO AET record count is unreasonable.");

        int count = checked((int)countValue);
        for (int i = 0; i < count; i++)
        {
            int slot = checked(36 + i * 4);
            int recordOffset = data.Relative(slot, data.I32(slot));
            data.CheckRange(recordOffset, 16);
            Records.Add(ReadRecord(data, recordOffset, i));
        }
    }

    private static FgoAetRecord ReadRecord(Reader data, int offset, int index)
    {
        byte typeValue = data.Byte(offset);
        if (typeValue > 2)
            throw new InvalidDataException($"Unsupported FGO AET record type {typeValue} at 0x{offset:X}.");

        var record = new FgoAetRecord
        {
            Index = index,
            Offset = offset,
            Type = (FgoAetRecordType)typeValue,
            RawHeader = data.U32(offset),
            ParentIndex = data.I32(offset + 4),
            NameOffset = data.Relative(offset + 8, data.I32(offset + 8)),
            Name = data.StringAt(data.Relative(offset + 8, data.I32(offset + 8)))
        };

        data.CheckRange(offset, record.Type == FgoAetRecordType.Scene ? 72 : 56);
        record.Attribute = data.U32(offset + 24);
        record.Value0 = data.Float(offset + 28);
        record.Value1 = data.Float(offset + 32);
        record.Color = data.U32(offset + 36);
        record.Width = data.U32(offset + 40);
        record.Height = data.U32(offset + 44);
        record.Value2 = data.Float(offset + 48);

        if (record.Type == FgoAetRecordType.Scene)
        {
            record.ChildCount = data.U32(offset + 68);
            if (record.ChildCount > 100000)
                throw new InvalidDataException("FGO AET child count is unreasonable.");

            int childOffset = checked(offset + 72);
            for (int i = 0; i < record.ChildCount; i++)
            {
                int child = checked(childOffset + i * 56);
                data.CheckRange(child, 56);
                record.Children.Add(new FgoAetChild
                {
                    Index = i,
                    Offset = child,
                    NameOffset = data.Relative(child, data.I32(child)),
                    Name = data.StringAt(data.Relative(child, data.I32(child))),
                    PropertyOffset = data.Relative(child + 4, data.I32(child + 4)),
                    PropertyName = data.StringAt(data.Relative(child + 4, data.I32(child + 4))),
                    ParentIndex = data.I32(child + 8),
                    Flags = data.U32(child + 12),
                    Value0 = data.I32(child + 16),
                    Value1 = data.I32(child + 20),
                    Kind = data.U32(child + 24),
                    Opacity = data.Float(child + 28),
                    Scale = data.Float(child + 32),
                    Position = data.Float(child + 36),
                    Rotation = data.Float(child + 40),
                    Value2 = data.Float(child + 44),
                    Value3 = data.Float(child + 48),
                    NestedOffset = data.Relative(child + 52, data.I32(child + 52))
                });
            }
        }
        else if (record.Type == FgoAetRecordType.Asset)
        {
            record.SourceCount = data.U32(offset + 52);
            if (record.SourceCount > 100000)
                throw new InvalidDataException("FGO AET source count is unreasonable.");

            int sourceOffset = checked(offset + 56);
            for (int i = 0; i < record.SourceCount; i++)
            {
                int source = checked(sourceOffset + i * 16);
                data.CheckRange(source, 16);
                record.Sources.Add(new FgoAetSource
                {
                    Index = i,
                    Offset = source,
                    NameOffset = data.Relative(source, data.I32(source)),
                    Name = data.StringAt(data.Relative(source, data.I32(source))),
                    PathOffset = data.Relative(source + 4, data.I32(source + 4)),
                    Path = data.StringAt(data.Relative(source + 4, data.I32(source + 4))),
                    Value0 = data.U32(source + 8),
                    Value1 = data.U32(source + 12)
                });
            }
        }

        return record;
    }

    public override void Write(EndianBinaryWriter writer, ISection section = null) =>
        throw new NotSupportedException("FGO AET export is not implemented.");

    private sealed class Reader
    {
        private readonly byte[] mData;
        public bool BigEndian { get; set; }

        public Reader(byte[] data) => mData = data;

        public byte Byte(int offset)
        {
            CheckRange(offset, 1);
            return mData[offset];
        }

        public uint U32(int offset)
        {
            CheckRange(offset, 4);
            return BigEndian
                ? BinaryPrimitives.ReadUInt32BigEndian(mData.AsSpan(offset, 4))
                : BinaryPrimitives.ReadUInt32LittleEndian(mData.AsSpan(offset, 4));
        }

        public int I32(int offset) => unchecked((int)U32(offset));

        public float Float(int offset) =>
            BitConverter.Int32BitsToSingle(I32(offset));

        public int Relative(int fieldOffset, int relative)
        {
            if (relative == 0)
                return 0;
            long target = (long)fieldOffset + relative;
            if (target < 0 || target > int.MaxValue)
                throw new InvalidDataException("FGO AET relative pointer is outside the file.");
            CheckRange((int)target, 1);
            return (int)target;
        }

        public string StringAt(int offset)
        {
            if (offset == 0)
                return string.Empty;
            CheckRange(offset, 1);
            int end = offset;
            while (end < mData.Length && mData[end] != 0)
                end++;
            if (end == mData.Length)
                throw new InvalidDataException("Unterminated FGO AET string.");
            return Encoding.UTF8.GetString(mData, offset, end - offset);
        }

        public void CheckRange(int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > mData.Length - length)
                throw new InvalidDataException("FGO AET pointer is outside the file.");
        }
    }
}

public enum FgoAetRecordType : byte
{
    Empty = 0,
    Scene = 1,
    Asset = 2
}

public sealed class FgoAetRecord
{
    public int Index { get; internal set; }
    public int Offset { get; internal set; }
    public FgoAetRecordType Type { get; internal set; }
    public uint RawHeader { get; internal set; }
    public int ParentIndex { get; internal set; }
    public int NameOffset { get; internal set; }
    public string Name { get; internal set; }
    public uint Attribute { get; internal set; }
    public float Value0 { get; internal set; }
    public float Value1 { get; internal set; }
    public uint Color { get; internal set; }
    public uint Width { get; internal set; }
    public uint Height { get; internal set; }
    public float Value2 { get; internal set; }
    public uint ChildCount { get; internal set; }
    public uint SourceCount { get; internal set; }
    public List<FgoAetChild> Children { get; } = new();
    public List<FgoAetSource> Sources { get; } = new();
}

public sealed class FgoAetChild
{
    public int Index { get; internal set; }
    public int Offset { get; internal set; }
    public int NameOffset { get; internal set; }
    public string Name { get; internal set; }
    public int PropertyOffset { get; internal set; }
    public string PropertyName { get; internal set; }
    public int ParentIndex { get; internal set; }
    public uint Flags { get; internal set; }
    public int Value0 { get; internal set; }
    public int Value1 { get; internal set; }
    public uint Kind { get; internal set; }
    public float Opacity { get; internal set; }
    public float Scale { get; internal set; }
    public float Position { get; internal set; }
    public float Rotation { get; internal set; }
    public float Value2 { get; internal set; }
    public float Value3 { get; internal set; }
    public int NestedOffset { get; internal set; }

    /// <summary>Gets the static X coordinate used by the FGO runtime.</summary>
    /// <remarks>The exported value is stored in the field historically named opacity.</remarks>
    public float StaticPositionX => Opacity;

    /// <summary>Gets the static Y coordinate used by the FGO runtime.</summary>
    public float StaticPositionY => Opacity + Scale;

    /// <summary>Gets the static Z/depth coordinate used by the FGO runtime.</summary>
    public float StaticPositionZ => Position;

    /// <summary>Gets the static uniform scale used by the FGO runtime.</summary>
    public float StaticScale => Math.Abs(Rotation) < float.Epsilon ? 1.0f : 1.0f / Rotation;
}

public sealed class FgoAetSource
{
    public int Index { get; internal set; }
    public int Offset { get; internal set; }
    public int NameOffset { get; internal set; }
    public string Name { get; internal set; }
    public int PathOffset { get; internal set; }
    public string Path { get; internal set; }
    public uint Value0 { get; internal set; }
    public uint Value1 { get; internal set; }
}
