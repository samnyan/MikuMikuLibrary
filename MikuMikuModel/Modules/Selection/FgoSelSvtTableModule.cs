using MikuMikuLibrary.IO;
using MikuMikuLibrary.Selection;

namespace MikuMikuModel.Modules.Selection;

public sealed class FgoSelSvtTableModule : FormatModule<FgoSelSvtTable>
{
    public override IReadOnlyList<FormatExtension> Extensions { get; } = new[]
    {
        new FormatExtension("FGO servant selection table", "bin", FormatExtensionFlags.Import)
    };

    public override bool Match(string fileName) =>
        Path.GetFileName(fileName).Equals("sel_svt.bin", StringComparison.OrdinalIgnoreCase);

    public override bool Match(byte[] buffer) =>
        buffer.Length >= 0x28 && BitConverter.ToUInt32(buffer, 0) >= 2;

    public override FgoSelSvtTable Import(string filePath) => BinaryFile.Load<FgoSelSvtTable>(filePath);

    protected override FgoSelSvtTable ImportCore(Stream source, string fileName) =>
        BinaryFile.Load<FgoSelSvtTable>(source, true);

    protected override void ExportCore(FgoSelSvtTable model, Stream destination, string fileName) =>
        throw new NotSupportedException("FGO selection tables are read-only");
}
