using MikuMikuLibrary.IO;
using MikuMikuLibrary.IO.Common;
using MikuMikuLibrary.IO.Sections;

namespace MikuMikuLibrary.MasterTables;

/// <summary>
/// FATE/Grand Order Arcade master table exported as UTF-8 key/value text.
///
/// The files are commonly stored with a <c>.bin</c> extension even though
/// their contents are text, for example:
/// <c>asset_table.0.svt_id=1</c>.
/// </summary>
public sealed class FgoMasterTable : BinaryFile
{
    private const int ProbeLimit = 64 * 1024;

    public string TableName { get; private set; } = string.Empty;
    /// <summary>The original UTF-8 text, retained so a normal Export preserves the source table.</summary>
    public string Text { get; private set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.Ordinal);
    public List<FgoMasterTableRow> Rows { get; } = new();

    /// <summary>All field names in first-seen order, useful for a dynamic table view.</summary>
    public IReadOnlyList<string> Columns => mColumns;

    private readonly List<string> mColumns = new();

    public override BinaryFileFlags Flags => BinaryFileFlags.Load;
    public override Endianness Endianness => Endianness.Little;

    public override void Read(EndianBinaryReader reader, ISection section = null)
    {
        if (!reader.BaseStream.CanSeek)
            throw new InvalidDataException("FGO master table requires a seekable stream");

        reader.BaseStream.Position = 0;
        using var buffer = new MemoryStream();
        reader.BaseStream.CopyTo(buffer);
        Parse(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    public override void Write(EndianBinaryWriter writer, ISection section = null) =>
        throw new NotSupportedException("FGO master tables are read-only");

    /// <summary>Checks whether a stream looks like an FGO key/value master table.</summary>
    public static bool IsTextTable(Stream source)
    {
        if (!source.CanSeek)
            return false;

        long position = source.Position;
        try
        {
            source.Position = 0;
            var data = new byte[Math.Min(ProbeLimit, checked((int)Math.Max(0, source.Length)))];
            int read = source.Read(data, 0, data.Length);
            if (read == 0)
                return false;

            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(data, 0, read);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            int keyValueLines = 0;
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = line[..separator];
                string[] parts = key.Split('.');
                if (parts.Length >= 3 &&
                    (parts[1] == "data_list" || int.TryParse(parts[1], out _)))
                    keyValueLines++;
            }

            return keyValueLines > 0;
        }
        finally
        {
            source.Position = position;
        }
    }

    private void Parse(string text)
    {
        Text = text;
        TableName = string.Empty;
        Metadata.Clear();
        Rows.Clear();
        mColumns.Clear();
        var rowMap = new Dictionary<int, FgoMasterTableRow>();

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            int separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            string key = line[..separator];
            string value = line[(separator + 1)..];
            string[] parts = key.Split('.');

            if (parts.Length < 3)
            {
                Metadata[key] = value;
                continue;
            }

            TableName = parts[0];
            string rowPart = parts[1];
            string fieldName = string.Join('.', parts.Skip(2));

            if (rowPart.Equals("data_list", StringComparison.Ordinal))
            {
                Metadata[fieldName] = value;
                continue;
            }

            if (!int.TryParse(rowPart, out int rowIndex))
                continue;

            if (!rowMap.TryGetValue(rowIndex, out var row))
            {
                row = new FgoMasterTableRow(rowIndex);
                rowMap[rowIndex] = row;
            }

            if (!mColumns.Contains(fieldName, StringComparer.Ordinal))
                mColumns.Add(fieldName);

            row.Fields[fieldName] = value;
        }

        Rows.AddRange(rowMap.Values.OrderBy(row => row.Index));
    }
}

public sealed class FgoMasterTableRow
{
    public int Index { get; }
    public Dictionary<string, string> Fields { get; } = new(StringComparer.Ordinal);

    public FgoMasterTableRow(int index)
    {
        Index = index;
    }

    public int? GetInt(string key) => Fields.TryGetValue(key, out string value) && int.TryParse(value, out int result)
        ? result
        : null;

    public long? GetLong(string key) => Fields.TryGetValue(key, out string value) && long.TryParse(value, out long result)
        ? result
        : null;

    public string GetString(string key) => Fields.TryGetValue(key, out string value) ? value : null;
}
