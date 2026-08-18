using MikuMikuLibrary.Archives;
using MikuMikuLibrary.IO;

namespace MikuMikuModel.Modules.Archives;

/// <summary>Module for FATE/Grand Order Arcade <c>FARc</c> archives.</summary>
public sealed class FgoFarcArchiveModule : FormatModule<FgoFarcArchive>
{
    /// <inheritdoc />
    public override IReadOnlyList<FormatExtension> Extensions { get; } = new[]
    {
        new FormatExtension("FGO Arcade FArc Archive", "farc", FormatExtensionFlags.Import | FormatExtensionFlags.Export)
    };

    /// <inheritdoc />
    public override bool Match(byte[] buffer) =>
        buffer.Length >= 4 && Encoding.UTF8.GetString(buffer, 0, 4) == "FARc";

    /// <inheritdoc />
    public override FgoFarcArchive Import(string filePath) =>
        BinaryFile.Load<FgoFarcArchive>(filePath);

    /// <inheritdoc />
    protected override FgoFarcArchive ImportCore(Stream source, string fileName) =>
        BinaryFile.Load<FgoFarcArchive>(source, true);

    /// <inheritdoc />
    protected override void ExportCore(FgoFarcArchive model, Stream destination, string fileName) =>
        model.Save(destination, true);
}
