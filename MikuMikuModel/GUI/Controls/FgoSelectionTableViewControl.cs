using System.Globalization;
using MikuMikuLibrary.MasterTables;
using MikuMikuLibrary.Selection;

namespace MikuMikuModel.GUI.Controls;

public sealed class FgoSelectionTableViewControl : UserControl
{
    private readonly DataGridView mGrid = new();

    public FgoSelectionTableViewControl(FgoSelSvtTable table)
    {
        InitializeGrid();
        mGrid.Columns.Add("Index", "Index");
        mGrid.Columns.Add("Key", "Key");
        mGrid.Columns.Add("Id", "Id");
        for (var i = 0; i < 18; i++)
            mGrid.Columns.Add($"Field{i:00}", $"Field {i:00}");

        foreach (var (record, index) in table.Records.Select((value, index) => (value, index)))
        {
            var values = new object[21];
            values[0] = index;
            values[1] = $"0x{record.Key:X16}";
            values[2] = $"0x{record.Id:X8}";
            for (var i = 0; i < record.Fields.Length; i++)
                values[i + 3] = FormatWord(record.Fields[i]);
            mGrid.Rows.Add(values);
        }
    }

    public FgoSelectionTableViewControl(FgoSelCommonTable table)
    {
        InitializeGrid();
        mGrid.Columns.Add("Index", "Index");
        mGrid.Columns.Add("Name", "Name");
        mGrid.Columns.Add("ValueSize", "Value size");
        mGrid.Columns.Add("ValueType", "Value type");
        mGrid.Columns.Add("ValueOffset", "Value offset");
        mGrid.Columns.Add("Values", "Values");

        foreach (var property in table.Properties)
        {
            mGrid.Rows.Add(
                property.Index,
                property.Name,
                property.ValueSize,
                property.ValueType,
                $"0x{property.ValueOffset:X}",
                FormatValues(property));
        }
    }

    public FgoSelectionTableViewControl(FgoMasterTable table)
    {
        InitializeGrid();
        mGrid.Columns.Add("Index", "Index");
        foreach (string column in table.Columns)
            mGrid.Columns.Add(column, column);

        foreach (var row in table.Rows)
        {
            var values = new object[table.Columns.Count + 1];
            values[0] = row.Index;
            for (var i = 0; i < table.Columns.Count; i++)
                values[i + 1] = row.Fields.TryGetValue(table.Columns[i], out string value) ? value : string.Empty;

            mGrid.Rows.Add(values);
        }
    }

    private void InitializeGrid()
    {
        Dock = DockStyle.Fill;
        mGrid.Dock = DockStyle.Fill;
        mGrid.ReadOnly = true;
        mGrid.AllowUserToAddRows = false;
        mGrid.AllowUserToDeleteRows = false;
        mGrid.AllowUserToResizeRows = false;
        mGrid.AutoGenerateColumns = false;
        mGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        mGrid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
        mGrid.RowHeadersVisible = false;
        mGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        mGrid.MultiSelect = false;
        Controls.Add(mGrid);
    }

    private static string FormatValues(FgoSelCommonProperty property)
    {
        var words = new List<string>();
        for (var i = 0; i + 4 <= property.Values.Length; i += 4)
        {
            var word = BitConverter.ToUInt32(property.Values, i);
            words.Add(FormatWord(word));
        }
        return property.Values.Length % 4 == 0
            ? $"[{string.Join(", ", words)}]"
            : Convert.ToHexString(property.Values);
    }

    private static string FormatWord(uint word)
    {
        var value = BitConverter.UInt32BitsToSingle(word);
        return float.IsFinite(value)
            ? $"0x{word:X8} / {value.ToString("0.######", CultureInfo.InvariantCulture)}"
            : $"0x{word:X8}";
    }
}
