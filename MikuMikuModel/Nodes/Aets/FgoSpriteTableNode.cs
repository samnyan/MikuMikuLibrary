using MikuMikuLibrary.Aets.Resources;
using MikuMikuModel.Modules;
using MikuMikuModel.GUI.Controls;
using MikuMikuModel.Nodes.IO;
using MikuMikuModel.Resources;

namespace MikuMikuModel.Nodes.Aets;

/// <summary>Read-only node for an FGO Sprite table.</summary>
public sealed class FgoSpriteTableNode : BinaryFileNode<FgoSpriteTable>
{
    /// <inheritdoc />
    public override NodeFlags Flags => NodeFlags.Add;

    /// <inheritdoc />
    public override Bitmap Image => ResourceStore.LoadBitmap("Icons/TextureSet.png");

    /// <summary>Gets the number of Sprite records.</summary>
    [Category("Sprite table")]
    public int RecordCount => Data.RecordCount;

    /// <summary>Gets the number of atlas textures.</summary>
    [Category("Sprite table")]
    public int TextureCount => Data.TextureNames.Count;

    protected override void Initialize()
    {
    }

    protected override void PopulateCore()
    {
        foreach (var entry in Data.Entries)
            Nodes.Add(new FgoSpriteTableEntryNode($"{entry.Index}: {entry.Name}", entry));
    }

    protected override void SynchronizeCore()
    {
    }

    public override Control Control => new FgoSpriteTableViewControl(Data);

    public FgoSpriteTableNode(string name, FgoSpriteTable data) : base(name, data)
    {
    }

    public FgoSpriteTableNode(string name, Func<Stream> streamGetter) : base(name, streamGetter)
    {
    }
}

/// <summary>Displays one FGO Sprite table record in the property grid.</summary>
public sealed class FgoSpriteTableEntryNode : Node<FgoSpriteEntry>
{
    public override NodeFlags Flags => NodeFlags.None;

    public override Bitmap Image => ResourceStore.LoadBitmap("Icons/Texture.png");

    private FgoSpriteResolution Resolution => AetResourceContext.Instance.Resolve(Data.Name);

    [Category("Sprite")]
    public string SpriteName => Data.Name;

    [Category("Sprite")]
    public string TextureName => Data.TextureName;

    [Category("Sprite")]
    public int TextureIndex => Data.TextureIndex;

    [Category("Sprite")]
    public string AtlasSize => $"{Data.AtlasWidth}x{Data.AtlasHeight}";

    [Category("Sprite")]
    public string Rectangle => $"{Data.X0},{Data.Y0} - {Data.X1},{Data.Y1}";

    [Category("Sprite")]
    public uint ResourceIndex => Data.ResourceIndex;

    [Category("Resource")]
    public string ResourceStatus => Resolution == null ? "Not found" : "Resolved";

    [Category("Resource")]
    public string ResourcePackage => Resolution?.Package.Name ?? string.Empty;

    [Category("Resource")]
    public string ResourcePath => Resolution?.Package.ArchivePath ?? string.Empty;

    [Category("Resource")]
    public string ResourceSprite => Resolution?.Entry.Name ?? string.Empty;

    protected override void Initialize()
    {
        AddCustomHandler("Export", ExportSprite, Keys.Control | Keys.E);
    }

    private void ExportSprite()
    {
        string fileName = string.Concat(Data.Name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "sprite";

        string filePath = ModuleExportUtilities.SelectModuleExport<Bitmap>(
            "Select a file to export to.", fileName);
        if (string.IsNullOrEmpty(filePath))
            return;

        var resolution = Resolution;
        if (resolution == null)
        {
            MessageBox.Show($"Sprite '{Data.Name}' could not be resolved.",
                Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!FgoSpriteBitmap.TryCrop(resolution, out var bitmap))
        {
            MessageBox.Show($"Sprite '{Data.Name}' has an invalid rectangle.",
                Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using (bitmap)
            FormatModuleRegistry.ModulesByType[typeof(Bitmap)].Export(bitmap, filePath);
    }

    protected override void PopulateCore()
    {
    }

    protected override void SynchronizeCore()
    {
    }

    public override Control Control
    {
        get
        {
            var resolution = AetResourceContext.Instance.Resolve(Data.Name);
            AetAssetPreviewControl.Instance.SetSprite(Data.Name, resolution);
            return AetAssetPreviewControl.Instance;
        }
    }

    public FgoSpriteTableEntryNode(string name, FgoSpriteEntry data) : base(name, data)
    {
    }
}
