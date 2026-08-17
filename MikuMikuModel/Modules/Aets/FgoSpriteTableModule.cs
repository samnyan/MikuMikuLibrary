using MikuMikuLibrary.Aets.Resources;
using MikuMikuLibrary.IO;

namespace MikuMikuModel.Modules.Aets;

/// <summary>Importer for FGO Arcade <c>spr_*_table.bin</c> Sprite tables.</summary>
public sealed class FgoSpriteTableModule : FormatModule<FgoSpriteTable>
{
    /// <inheritdoc />
    public override IReadOnlyList<FormatExtension> Extensions { get; } = new[]
    {
        new FormatExtension("FGO Sprite table", "bin", FormatExtensionFlags.Import)
    };

    /// <inheritdoc />
    public override bool Match(string fileName) =>
        Path.GetFileName(fileName).StartsWith("spr_", StringComparison.OrdinalIgnoreCase) &&
        fileName.EndsWith("_table.bin", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Match(byte[] buffer) => FgoSpriteTable.IsSpriteTable(buffer);

    /// <inheritdoc />
    public override FgoSpriteTable Import(string filePath) => BinaryFile.Load<FgoSpriteTable>(filePath);

    /// <inheritdoc />
    protected override FgoSpriteTable ImportCore(Stream source, string fileName) =>
        BinaryFile.Load<FgoSpriteTable>(source, leaveOpen: true);

    /// <inheritdoc />
    protected override void ExportCore(FgoSpriteTable model, Stream destination, string fileName) =>
        throw new NotSupportedException("FGO Sprite table export is not implemented.");
}
