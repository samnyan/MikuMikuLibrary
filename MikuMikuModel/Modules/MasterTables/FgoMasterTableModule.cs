using MikuMikuLibrary.IO;
using MikuMikuLibrary.MasterTables;

namespace MikuMikuModel.Modules.MasterTables;

/// <summary>Import-only module for FGO Arcade's text master tables.</summary>
public sealed class FgoMasterTableModule : FormatModule<FgoMasterTable>
{
    public override IReadOnlyList<FormatExtension> Extensions { get; } = new[]
    {
        new FormatExtension("FGO master table (key/value text)", "bin", FormatExtensionFlags.Import)
    };

    public override bool Match(string fileName)
    {
        string name = Path.GetFileName(fileName);
        return name.StartsWith("arms_mst_", StringComparison.OrdinalIgnoreCase);
    }

    public override bool Match(byte[] buffer)
    {
        // Filename matching handles normal mst_data entries. This additional
        // check allows extracted files with an unusual name to be selected by
        // callers that provide a sufficiently large probe buffer.
        if (buffer.Length == 0)
            return false;

        int equals = Array.IndexOf(buffer, (byte)'=');
        return equals > 0 && Array.IndexOf(buffer, (byte)'.') >= 0;
    }

    public override FgoMasterTable Import(string filePath) => BinaryFile.Load<FgoMasterTable>(filePath);

    protected override FgoMasterTable ImportCore(Stream source, string fileName) =>
        BinaryFile.Load<FgoMasterTable>(source, true);

    protected override void ExportCore(FgoMasterTable model, Stream destination, string fileName) =>
        throw new NotSupportedException("FGO master tables are read-only");
}
