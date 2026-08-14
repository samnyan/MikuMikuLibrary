using MikuMikuLibrary.IO;
using MikuMikuLibrary.Selection;

namespace MikuMikuModel.Modules.Selection;

public sealed class FgoSelCommonTableModule : FormatModule<FgoSelCommonTable>
{
    public override IReadOnlyList<FormatExtension> Extensions { get; } = new[]
    {
        new FormatExtension("FGO common selection properties", "bin", FormatExtensionFlags.Import)
    };

    public override bool Match(string fileName) =>
        Path.GetFileName(fileName).Equals("sel_common.bin", StringComparison.OrdinalIgnoreCase);

    public override bool Match(byte[] buffer) =>
        buffer.Length >= 8 && BitConverter.ToUInt32(buffer, 0) == 0 && BitConverter.ToUInt32(buffer, 4) > 0;

    public override FgoSelCommonTable Import(string filePath) => BinaryFile.Load<FgoSelCommonTable>(filePath);

    protected override FgoSelCommonTable ImportCore(Stream source, string fileName) =>
        BinaryFile.Load<FgoSelCommonTable>(source, true);

    protected override void ExportCore(FgoSelCommonTable model, Stream destination, string fileName) =>
        throw new NotSupportedException("FGO selection tables are read-only");
}
