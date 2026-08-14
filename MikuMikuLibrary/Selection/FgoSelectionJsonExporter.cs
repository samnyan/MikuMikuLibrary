namespace MikuMikuLibrary.Selection;

using MikuMikuLibrary.MasterTables;

/// <summary>Exports the reverse-engineered FGO Arcade selection tables as readable JSON.</summary>
public static class FgoSelectionJsonExporter
{
    private static readonly JsonSerializerOptions sOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(FgoSelSvtTable table)
    {
        var document = new
        {
            format = "fgo.sel_svt",
            table.Version,
            legacyRecordCount = table.LegacyRecordCount,
            legacyRecordOffset = table.LegacyRecordOffset,
            recordCount = table.RecordCount,
            recordOffset = table.RecordOffset,
            records = table.Records.Select((record, index) => new
            {
                index,
                key = record.Key,
                keyHigh = record.KeyHigh,
                keyHex = $"0x{record.Key:X16}",
                id = record.Id,
                fields = record.Fields.Select((value, fieldIndex) => new
                {
                    index = fieldIndex,
                    offset = 8 + fieldIndex * sizeof(uint),
                    uint32 = value,
                    float32 = ToFiniteFloat(value),
                    rawHex = $"0x{value:X8}"
                }).ToArray()
            }).ToArray()
        };

        return JsonSerializer.Serialize(document, sOptions);
    }

    public static string Serialize(FgoSelCommonTable table)
    {
        var document = new
        {
            format = "fgo.sel_common",
            table.Version,
            propertyCount = table.PropertyCount,
            properties = table.Properties.Select(property => new
            {
                property.Index,
                property.Name,
                property.ValueSize,
                property.ValueType,
                property.ValueOffset,
                value = DescribeValue(property)
            }).ToArray()
        };

        return JsonSerializer.Serialize(document, sOptions);
    }

    public static string Serialize(FgoMasterTable table)
    {
        var document = new
        {
            format = "fgo.master_table",
            tableName = table.TableName,
            metadata = table.Metadata,
            columns = table.Columns,
            rows = table.Rows.Select(row => new
            {
                index = row.Index,
                fields = row.Fields
            }).ToArray()
        };

        return JsonSerializer.Serialize(document, sOptions);
    }

    private static object DescribeValue(FgoSelCommonProperty property)
    {
        var words = property.Values.Length % sizeof(uint) == 0
            ? Enumerable.Range(0, property.Values.Length / sizeof(uint))
                .Select(offset => BitConverter.ToUInt32(property.Values, offset * sizeof(uint)))
                .ToArray()
            : Array.Empty<uint>();

        return new
        {
            type = property.ValueType,
            rawHex = Convert.ToHexString(property.Values),
            uint32 = words,
            float32 = words.Select(ToFiniteFloat).ToArray()
        };
    }

    private static float? ToFiniteFloat(uint value)
    {
        var result = BitConverter.UInt32BitsToSingle(value);
        return float.IsFinite(result) ? result : null;
    }
}
