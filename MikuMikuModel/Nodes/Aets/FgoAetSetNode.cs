using MikuMikuLibrary.Aets;
using MikuMikuLibrary.Aets.Resources;
using MikuMikuModel.GUI.Controls;
using MikuMikuModel.Nodes.IO;
using MikuMikuModel.Resources;

namespace MikuMikuModel.Nodes.Aets;

public sealed class FgoAetSetNode : BinaryFileNode<FgoAetSet>
{
    public override NodeFlags Flags => NodeFlags.Add;
    public override Bitmap Image => ResourceStore.LoadBitmap("Icons/Folder.png");

    protected override void Initialize()
    {
    }

    protected override void PopulateCore()
    {
        foreach (var record in Data.Records)
            Nodes.Add(new FgoAetRecordNode($"{record.Index}: {record.Name}", record, Data));
    }

    protected override void SynchronizeCore()
    {
    }

    public FgoAetSetNode(string name, FgoAetSet data) : base(name, data)
    {
    }

    public FgoAetSetNode(string name, Func<Stream> streamGetter) : base(name, streamGetter)
    {
    }
}

public sealed class FgoAetRecordNode : Node<FgoAetRecord>
{
    private readonly FgoAetSet mSet;
    // Flags is queried by Node<T>'s constructor while it assigns Name.  It
    // must not access Data there, otherwise Data -> Synchronize -> Flags
    // recurses before construction has completed.  Empty records simply
    // populate to an empty node when expanded.
    public override NodeFlags Flags => NodeFlags.Add;

    [Category("AET")]
    public FgoAetRecordType Type => Data.Type;

    [Category("AET")]
    public int Index => Data.Index;

    [Category("AET")]
    public int ParentIndex => Data.ParentIndex;

    [Category("AET")]
    public int Offset => Data.Offset;

    [Category("AET")]
    public uint Attribute => Data.Attribute;

    [Category("AET")]
    public float Value0 => Data.Value0;

    [Category("AET")]
    public float Value1 => Data.Value1;

    [Category("AET")]
    public uint Color => Data.Color;

    [Category("AET")]
    public uint Width => Data.Width;

    [Category("AET")]
    public uint Height => Data.Height;

    [Category("AET")]
    public int ChildCount => Data.Children.Count;

    [Category("AET")]
    public int SourceCount => Data.Sources.Count;

    protected override void Initialize()
    {
    }

    protected override void PopulateCore()
    {
        foreach (var child in Data.Children)
            Nodes.Add(new FgoAetChildNode($"{child.Index}: {child.Name}", child));

        foreach (var source in Data.Sources)
            Nodes.Add(new FgoAetSourceNode($"{source.Index}: {source.Name}", source));
    }

    protected override void SynchronizeCore()
    {
    }

    public FgoAetRecordNode(string name, FgoAetRecord data, FgoAetSet set) : base(name, data)
    {
        mSet = set ?? throw new ArgumentNullException(nameof(set));
    }

    public override Control Control => Data.Type == FgoAetRecordType.Scene
        ? GetScenePreview()
        : base.Control;

    private Control GetScenePreview()
    {
        AetScenePreviewControl.Instance.SetScene(mSet, Data);
        return AetScenePreviewControl.Instance;
    }
}

public sealed class FgoAetChildNode : Node<FgoAetChild>
{
    public override NodeFlags Flags => NodeFlags.None;

    private FgoSpriteResolution Resolution =>
        AetResourceContext.Instance.Resolve(Data.Name);

    public override Bitmap Image => IsSprite
        ? ResourceStore.LoadBitmap("Icons/Texture.png")
        : base.Image;

    private bool IsSprite => Data.Name.EndsWith(".pic", StringComparison.OrdinalIgnoreCase);

    [Category("Layer")]
    public string PropertyName => Data.PropertyName;

    [Category("Layer")]
    public int ParentIndex => Data.ParentIndex;

    [Category("Layer")]
    public uint FlagsValue => Data.Flags;

    [Category("Layer")]
    public uint Kind => Data.Kind;

    [Category("Layer")]
    public float Opacity => Data.Opacity;

    [Category("Layer")]
    public float Scale => Data.Scale;

    [Category("Layer")]
    public float Position => Data.Position;

    [Category("Layer")]
    public float Rotation => Data.Rotation;

    [Category("Layer")]
    public int NestedOffset => Data.NestedOffset;

    [Category("Asset")]
    public string ResourceStatus => !IsSprite ? string.Empty : Resolution == null ? "Not found" : "Resolved";

    [Category("Asset")]
    public string ResourcePackage => IsSprite ? Resolution?.Package.Name ?? string.Empty : string.Empty;

    [Category("Asset")]
    public string ResourcePath => IsSprite ? Resolution?.Package.ArchivePath ?? string.Empty : string.Empty;

    [Category("Asset")]
    public string ResourceSprite => IsSprite ? Resolution?.Entry.Name ?? string.Empty : string.Empty;

    protected override void Initialize()
    {
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
            if (!IsSprite)
                return base.Control;

            AetAssetPreviewControl.Instance.SetSprite(Data.Name, Resolution);
            return AetAssetPreviewControl.Instance;
        }
    }

    public FgoAetChildNode(string name, FgoAetChild data) : base(name, data)
    {
    }
}

public sealed class FgoAetSourceNode : Node<FgoAetSource>
{
    public override NodeFlags Flags => NodeFlags.None;

    public override Bitmap Image => ResourceStore.LoadBitmap("Icons/Texture.png");

    private FgoSpriteResolution Resolution =>
        AetResourceContext.Instance.Resolve(Data.Path, Data.Name);

    [Category("Asset")]
    public string Path => Data.Path;

    [Category("Asset")]
    public string ResourceStatus => Resolution == null ? "Not found" : "Resolved";

    [Category("Asset")]
    public string ResourcePackage => Resolution?.Package.Name ?? string.Empty;

    [Category("Asset")]
    public string ResourceSprite => Resolution?.Entry.Name ?? string.Empty;

    [Category("Asset")]
    public string ResourcePath => Resolution?.Package.ArchivePath ?? string.Empty;

    [Category("Asset")]
    public int ResourceTextureIndex => Resolution?.Entry.TextureIndex ?? -1;

    [Category("Asset")]
    public string ResourceRectangle => Resolution == null
        ? string.Empty
        : $"{Resolution.Entry.X0},{Resolution.Entry.Y0} - " +
          $"{Resolution.Entry.X1},{Resolution.Entry.Y1}";

    [Category("Asset")]
    public uint Value0 => Data.Value0;

    [Category("Asset")]
    public uint Value1 => Data.Value1;

    protected override void Initialize()
    {
    }

    protected override void PopulateCore()
    {
    }

    protected override void SynchronizeCore()
    {
    }

    public FgoAetSourceNode(string name, FgoAetSource data) : base(name, data)
    {
    }

    public override Control Control
    {
        get
        {
            AetAssetPreviewControl.Instance.SetSource(Data);
            return AetAssetPreviewControl.Instance;
        }
    }
}
