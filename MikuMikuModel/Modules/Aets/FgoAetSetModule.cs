using MikuMikuLibrary.Aets;
using MikuMikuLibrary.IO;

namespace MikuMikuModel.Modules.Aets;

/// <summary>Importer for FGO Arcade's custom AET set stored as *.bin.</summary>
public sealed class FgoAetSetModule : FormatModule<FgoAetSet>
{
    public override IReadOnlyList<FormatExtension> Extensions { get; } = new[]
    {
        new FormatExtension("FGO AET Set", "bin", FormatExtensionFlags.Import)
    };

    public override bool Match(string fileName) =>
        fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase);

    public override bool Match(byte[] buffer) =>
        buffer.Length >= 4 &&
        ((buffer[0] == 0x03 && buffer[1] == 0x01 && buffer[2] == 0x45 && buffer[3] == 0x41) ||
         (buffer[0] == 0x41 && buffer[1] == 0x45 && buffer[2] == 0x01 && buffer[3] == 0x03));

    public override FgoAetSet Import(string filePath) => BinaryFile.Load<FgoAetSet>(filePath);

    protected override FgoAetSet ImportCore(Stream source, string fileName) =>
        BinaryFile.Load<FgoAetSet>(source, true);

    protected override void ExportCore(FgoAetSet model, Stream destination, string fileName) =>
        throw new NotSupportedException("FGO AET export is not implemented.");
}
