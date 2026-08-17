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

    /// <summary>Gets the largest scene duration, in seconds.</summary>
    public float Duration => Records.Count == 0
        ? 0.0f
        : Records.Where(record => record.Type == FgoAetRecordType.Scene)
            .Select(record => record.Duration)
            .DefaultIfEmpty(0.0f)
            .Max();

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

        var nestedOffsets = Records.SelectMany(record => record.Children)
            .Select(child => child.NestedOffset)
            .Where(offset => offset > 0)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        foreach (var child in Records.SelectMany(record => record.Children))
        {
            if (child.NestedOffset <= 0)
                continue;

            int next = data.Length;
            foreach (int offset in nestedOffsets)
            {
                if (offset > child.NestedOffset)
                {
                    next = offset;
                    break;
                }
            }

            child.Properties.AddRange(ParseProperties(data, child.NestedOffset, next));
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
                int childRecordOffset = checked(childOffset + i * 56);
                data.CheckRange(childRecordOffset, 56);
                var child = new FgoAetChild
                {
                    Index = i,
                    Offset = childRecordOffset,
                    NameOffset = data.Relative(childRecordOffset, data.I32(childRecordOffset)),
                    Name = data.StringAt(data.Relative(childRecordOffset, data.I32(childRecordOffset))),
                    PropertyOffset = data.Relative(childRecordOffset + 4, data.I32(childRecordOffset + 4)),
                    PropertyName = data.StringAt(data.Relative(childRecordOffset + 4, data.I32(childRecordOffset + 4))),
                    ParentIndex = data.I32(childRecordOffset + 8),
                    Flags = data.U32(childRecordOffset + 12),
                    Value0 = data.I32(childRecordOffset + 16),
                    Value1 = data.I32(childRecordOffset + 20),
                    Kind = data.U32(childRecordOffset + 24),
                    Opacity = data.Float(childRecordOffset + 28),
                    Scale = data.Float(childRecordOffset + 32),
                    Position = data.Float(childRecordOffset + 36),
                    Rotation = data.Float(childRecordOffset + 40),
                    Value2 = data.Float(childRecordOffset + 44),
                    Value3 = data.Float(childRecordOffset + 48),
                    NestedOffset = data.Relative(childRecordOffset + 52, data.I32(childRecordOffset + 52))
                };
                record.Children.Add(child);
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

    private static IReadOnlyList<FgoAetProperty> ParseProperties(Reader data, int nestedOffset, int scanEnd)
    {
        if (nestedOffset <= 0 || nestedOffset >= data.Length - 48)
            return Array.Empty<FgoAetProperty>();

        // A properties block starts with a 0x30-byte header.  Its first
        // property is always at +0x1c; each property then stores a relative
        // name pointer, a key count and keyCount four-float key records:
        // frame, value, incoming tangent and outgoing tangent.  The native
        // aet::LayoutParameter evaluator uses the same four-float records.
        int candidateOffset = nestedOffset + 28;
        if (candidateOffset >= scanEnd ||
            !data.TryPropertyName(candidateOffset, out string firstName) ||
            !firstName.StartsWith("ADBE ", StringComparison.Ordinal))
            return Array.Empty<FgoAetProperty>();

        var result = new List<FgoAetProperty>();
        while (candidateOffset < scanEnd &&
               data.TryPropertyName(candidateOffset, out string name) &&
               name.StartsWith("ADBE ", StringComparison.Ordinal))
        {
            uint keyCount = data.U32(candidateOffset + 4);
            if (keyCount == 0 || keyCount > 100000)
                break;

            long valueEnd = (long)candidateOffset + 16L + keyCount * 16L;
            if (valueEnd > scanEnd || valueEnd > data.Length)
                break;

            var values = new List<float>();
            for (int position = candidateOffset + 8;
                 position < candidateOffset + 8 + keyCount * 16; position += 4)
            {
                float value = data.Float(position);
                if (!float.IsFinite(value))
                {
                    values.Clear();
                    break;
                }

                values.Add(value);
            }

            if (values.Count == 0)
                break;

            result.Add(new FgoAetProperty(name, checked((int)keyCount), values));
            // The exporter appends an eight-byte trailer to every property
            // entry (also for one-key/static values).
            candidateOffset = checked((int)valueEnd);
        }

        return result;
    }

    public override void Write(EndianBinaryWriter writer, ISection section = null) =>
        throw new NotSupportedException("FGO AET export is not implemented.");

    private sealed class Reader
    {
        private readonly byte[] mData;
        public bool BigEndian { get; set; }

        public int Length => mData.Length;
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

        public string StringAtOrNull(int offset)
        {
            if (offset < 0 || offset >= mData.Length || mData[offset] == 0)
                return null;

            int end = offset;
            while (end < mData.Length && mData[end] != 0)
                end++;

            if (end == mData.Length)
                return null;

            try
            {
                return Encoding.UTF8.GetString(mData, offset, end - offset);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        public int RelativeUnchecked(int fieldOffset, int relative)
        {
            long target = (long)fieldOffset + relative;
            return target < 0 || target >= mData.Length ? -1 : (int)target;
        }

        public bool TryPropertyName(int offset, out string name)
        {
            name = null;
            if (offset < 0 || offset > mData.Length - 4)
                return false;

            int target = RelativeUnchecked(offset, I32(offset));
            if (target < 0)
                return false;

            name = StringAtOrNull(target);
            return name != null;
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
    /// <summary>Gets the composition duration in seconds.</summary>
    public float Duration => float.IsFinite(Value0) && Value0 > 0.0f ? Value0 : 0.0f;
    /// <summary>Gets the composition frame rate.</summary>
    public float FrameRate => float.IsFinite(Value1) && Value1 > 0.0f ? Value1 : 60.0f;
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
    public List<FgoAetProperty> Properties { get; } = new();
    public bool IsVisible { get; set; } = true;

    // These four values are layer timing/export metadata, not transform
    // defaults.  In particular Rotation is normally 1.0 in every record and
    // must never be used as a visual rotation fallback.
    public float StaticPositionX => 0.0f;
    public float StaticPositionY => 0.0f;
    public float StaticPositionZ => 0.0f;
    public float StaticScale => 1.0f;

    /// <summary>Gets the layer start time in seconds.</summary>
    public float LayerStartTime => float.IsFinite(Opacity) ? Opacity : 0.0f;

    /// <summary>Gets the layer duration in seconds.</summary>
    public float LayerDuration => float.IsFinite(Scale) ? Scale : 0.0f;

    /// <summary>Gets the local timeline offset in seconds.</summary>
    public float LayerOffsetTime => float.IsFinite(Position) ? Position : 0.0f;

    /// <summary>Gets the local timeline speed multiplier.</summary>
    public float LayerTimeScale => float.IsFinite(Rotation) && Math.Abs(Rotation) > float.Epsilon
        ? Rotation
        : 1.0f;

    /// <summary>Determines whether the layer is active at the specified composition time.</summary>
    public bool IsActive(float time)
    {
        float start = LayerStartTime;
        float duration = LayerDuration;
        return duration >= 0.0f
            ? time >= start && time < start + duration
            : time >= start + duration && time < start;
    }

    /// <summary>Maps composition time into the layer's local timeline.</summary>
    public float ToLocalTime(float time) =>
        (time - LayerStartTime) * LayerTimeScale + LayerOffsetTime;

    /// <summary>Evaluates a named ADBE transform property at a time in seconds.</summary>
    public float EvaluateProperty(string name, float time, float fallback, float duration)
    {
        var property = Properties.FirstOrDefault(value =>
            value.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return property?.Evaluate(time, fallback, duration) ?? fallback;
    }

    /// <summary>Gets the animated X position, falling back to the static export fields.</summary>
    public float EvaluatePositionX(float time, float duration) =>
        EvaluateProperty("ADBE Position_0", time, StaticPositionX, duration);

    /// <summary>Gets the animated Y position, falling back to the static export fields.</summary>
    public float EvaluatePositionY(float time, float duration) =>
        EvaluateProperty("ADBE Position_1", time, StaticPositionY, duration);

    /// <summary>Gets the animated Z/depth position, falling back to the static export fields.</summary>
    public float EvaluatePositionZ(float time, float duration) =>
        EvaluateProperty("ADBE Position_2", time, StaticPositionZ, duration);

    /// <summary>Gets the animated X anchor point, falling back to the origin.</summary>
    public float EvaluateAnchorX(float time, float duration) =>
        EvaluateProperty("ADBE Anchor Point_0", time, 0.0f, duration);

    /// <summary>Gets the animated Y anchor point, falling back to the origin.</summary>
    public float EvaluateAnchorY(float time, float duration) =>
        EvaluateProperty("ADBE Anchor Point_1", time, 0.0f, duration);

    /// <summary>Gets the animated Z anchor/depth pivot.</summary>
    public float EvaluateAnchorZ(float time, float duration) =>
        EvaluateProperty("ADBE Anchor Point_2", time, 0.0f, duration);

    /// <summary>Gets the animated X-axis rotation used by 3D AET layers.</summary>
    public float EvaluateRotationX(float time, float duration) =>
        EvaluateProperty("ADBE Rotate X", time, 0.0f, duration);

    /// <summary>Gets the animated Y-axis rotation used by 3D AET layers.</summary>
    public float EvaluateRotationY(float time, float duration) =>
        EvaluateProperty("ADBE Rotate Y", time, 0.0f, duration);

    /// <summary>Gets the animated X orientation used by 3D AET layers.</summary>
    public float EvaluateOrientationX(float time, float duration) =>
        EvaluateProperty("ADBE Orientation_0", time, 0.0f, duration);

    /// <summary>Gets the animated Y orientation used by 3D AET layers.</summary>
    public float EvaluateOrientationY(float time, float duration) =>
        EvaluateProperty("ADBE Orientation_1", time, 0.0f, duration);

    /// <summary>Gets the animated Z orientation used by 3D AET layers.</summary>
    public float EvaluateOrientationZ(float time, float duration) =>
        EvaluateProperty("ADBE Orientation_2", time, 0.0f, duration);

    /// <summary>Gets the animated Z rotation, falling back to zero degrees.</summary>
    public float EvaluateRotation(float time, float duration) =>
        EvaluateProperty("ADBE Rotate Z", time, 0.0f, duration);

    /// <summary>Gets the animated opacity, falling back to full opacity.</summary>
    public float EvaluateOpacity(float time, float duration) =>
        EvaluateProperty("ADBE Opacity", time, 1.0f, duration);

    /// <summary>Gets the animated X scale, falling back to one.</summary>
    public float EvaluateScaleX(float time, float duration) =>
        EvaluateProperty("ADBE Scale_0", time, StaticScale, duration);

    /// <summary>Gets the animated Y scale, falling back to one.</summary>
    public float EvaluateScaleY(float time, float duration) =>
        EvaluateProperty("ADBE Scale_1", time, StaticScale, duration);

    /// <summary>Gets the animated Z scale used by 3D AET layers.</summary>
    public float EvaluateScaleZ(float time, float duration) =>
        EvaluateProperty("ADBE Scale_2", time, StaticScale, duration);
}

/// <summary>One parsed ADBE property attached to an FGO AET layer.</summary>
public sealed class FgoAetProperty
{
    public string Name { get; }
    /// <summary>Gets the number of key records in the source property.</summary>
    public int KeyCount { get; }
    /// <summary>Compatibility alias for the former inspector field.</summary>
    public int Type { get; }
    public IReadOnlyList<float> Values { get; }

    internal FgoAetProperty(string name, int type, IReadOnlyList<float> values)
    {
        Name = name;
        KeyCount = type;
        Type = type;
        Values = values;
    }

    /// <summary>Evaluates the best representation available in the exporter block.</summary>
    public float Evaluate(float time, float fallback, float duration)
    {
        if (Values.Count == 0)
            return fallback;
        if (Values.Count == 1)
            return Values[0];

        // Animated blocks are emitted as time/value/tangent quadruples.
        // Only use that interpretation when time values are monotonic and fit
        // the owning scene; otherwise use the first/last value envelope.
        if (Values.Count >= 4 && Values.Count % 4 == 0)
        {
            int count = Values.Count / 4;
            bool validKeys = true;
            for (int i = 0; i < count; i++)
            {
                float keyFrame = Values[i * 4];
                if (!float.IsFinite(keyFrame) ||
                    (i > 0 && keyFrame < Values[(i - 1) * 4]))
                {
                    validKeys = false;
                    break;
                }
            }

            if (validKeys)
            {
                if (time <= Values[0])
                    return Values[1];

                for (int i = 1; i < count; i++)
                {
                    int leftIndex = (i - 1) * 4;
                    int rightIndex = i * 4;
                    float leftFrame = Values[leftIndex];
                    float rightFrame = Values[rightIndex];
                    if (time <= rightFrame)
                    {
                        float left = Values[leftIndex + 1];
                        float right = Values[rightIndex + 1];
                        float amount = rightFrame <= leftFrame
                            ? 0.0f
                            : Math.Clamp((time - leftFrame) / (rightFrame - leftFrame), 0.0f, 1.0f);
                        float amount2 = amount * amount;
                        float amount3 = amount2 * amount;
                        float leftTangent = Values[leftIndex + 3];
                        float rightTangent = Values[rightIndex + 2];
                        float interval = rightFrame - leftFrame;
                        return (2.0f * amount3 - 3.0f * amount2 + 1.0f) * left +
                               (amount3 - 2.0f * amount2 + amount) * interval * leftTangent +
                               (-2.0f * amount3 + 3.0f * amount2) * right +
                               (amount3 - amount2) * interval * rightTangent;
                    }
                }

                return Values[^3];
            }
        }

        return Values.Count >= 2 ? Values[1] : Values[0];
    }
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
