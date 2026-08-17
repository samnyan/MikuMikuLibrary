using System.ComponentModel;
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
            Nodes.Add(new FgoAetRecordNode(FormatIndexedName(record.Index, record.Name), record, Data));
    }

    internal static string FormatIndexedName(int index, string name)
    {
        string prefix = $"{index}: ";
        if (name != null && name.StartsWith(prefix, StringComparison.Ordinal))
            name = name[prefix.Length..];

        return $"{prefix}{name}";
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
    public float Duration => Data.Duration;

    [Category("AET")]
    public float FrameRate => Data.FrameRate;

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
            Nodes.Add(new FgoAetChildNode(FgoAetSetNode.FormatIndexedName(child.Index, child.Name),
                child, mSet, Data, Data));

        foreach (var source in Data.Sources)
            Nodes.Add(new FgoAetSourceNode(FgoAetSetNode.FormatIndexedName(source.Index, source.Name),
                source));
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
    private readonly FgoAetSet mSet;
    private readonly FgoAetRecord mScene;
    private bool mIsComposition;

    private FgoAetRecord LinkedRecord => mSet?.Records.FirstOrDefault(record =>
        record.Index == (int)Data.Kind);

    private bool IsComposition => mIsComposition;

    public override NodeFlags Flags => IsComposition ? NodeFlags.Add : NodeFlags.None;

    private FgoSpriteResolution Resolution
    {
        get
        {
            if (LinkedRecord?.Type == FgoAetRecordType.Asset && LinkedRecord.Sources.Count > 0)
            {
                // Match the same authoritative source used by scene rendering;
                // do not resolve a duplicate layer label independently.
                return AetResourceContext.Instance.Resolve(LinkedRecord.Sources[0]);
            }

            return AetResourceContext.Instance.Resolve(Data.Name);
        }
    }

    public override Bitmap Image => IsSprite
        ? ResourceStore.LoadBitmap("Icons/Texture.png")
        : IsComposition
            ? ResourceStore.LoadBitmap("Icons/Folder.png")
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

    [Category("Timeline")]
    public float StartTime => Data.LayerStartTime;

    [Category("Timeline")]
    public float Duration => Data.LayerDuration;

    [Category("Timeline")]
    public float OffsetTime => Data.LayerOffsetTime;

    [Category("Timeline")]
    public float TimeScale => Data.LayerTimeScale;

    [Browsable(false)]
    public float RawOpacity => Data.Opacity;

    [Browsable(false)]
    public float RawScale => Data.Scale;

    [Browsable(false)]
    public float RawPosition => Data.Position;

    [Browsable(false)]
    public float RawRotation => Data.Rotation;

    [Category("Layer")]
    public int NestedOffset => Data.NestedOffset;

    [Category("Layer")]
    public bool Visible
    {
        get => Data.IsVisible;
        set
        {
            if (Data.IsVisible == value)
                return;

            Data.IsVisible = value;
            OnPropertyChanged();
            AetScenePreviewControl.Instance.RefreshVisibility();
        }
    }

    [Category("Layer")]
    public string LayerType => IsComposition ? "Composition" : IsSprite ? "Sprite asset" : "Layer";

    [Category("Animation")]
    public int PropertyCount => Data.Properties.Count;

    [Category("Asset")]
    public string ResourceStatus => !IsSprite ? string.Empty : Resolution == null ? "Not found" : "Resolved";

    [Category("Asset")]
    public string ResourcePackage => IsSprite ? Resolution?.Package.Name ?? string.Empty : string.Empty;

    [Category("Asset")]
    public string ResourcePath => IsSprite ? Resolution?.Package.ArchivePath ?? string.Empty : string.Empty;

    [Category("Asset")]
    public string ResourceTablePath => IsSprite ? Resolution?.Package.TablePath ?? string.Empty : string.Empty;

    [Category("Asset")]
    public int ResourceTableIndex => IsSprite ? Resolution?.Entry.Index ?? -1 : -1;

    [Category("Asset")]
    public string ResourceSprite => IsSprite ? Resolution?.Entry.Name ?? string.Empty : string.Empty;

    [Category("Asset")]
    public string ResourceTextureName => IsSprite ? Resolution?.Entry.TextureName ?? string.Empty : string.Empty;

    [Category("Asset")]
    public int ResourceTextureIndex => IsSprite ? Resolution?.Entry.TextureIndex ?? -1 : -1;

    [Category("Asset")]
    public string ResourceRectangle => !IsSprite || Resolution == null
        ? string.Empty
        : $"{Resolution.Entry.X0},{Resolution.Entry.Y0} - " +
          $"{Resolution.Entry.X1},{Resolution.Entry.Y1}";

    protected override void Initialize()
    {
        AddCustomHandler("Toggle visibility", () => Visible = !Visible);
    }

    protected override void PopulateCore()
    {
        if (!IsComposition)
            return;

        foreach (var child in LinkedRecord.Children)
            Nodes.Add(new FgoAetChildNode(FgoAetSetNode.FormatIndexedName(child.Index, child.Name), child, mSet,
                LinkedRecord, mScene));
    }

    protected override void SynchronizeCore()
    {
    }

    public override Control Control
    {
        get
        {
            // A layer/group remains part of its parent scene. Keep the scene
            // preview active when the tree selection moves below the scene;
            // only the selected layer changes in the canvas.
            if (mScene != null)
            {
                var preview = AetScenePreviewControl.Instance;
                preview.ShowScene(mSet, mScene, Data);
                return preview;
            }

            if (IsSprite)
            {
                AetAssetPreviewControl.Instance.SetSprite(Data.Name, Resolution);
                return AetAssetPreviewControl.Instance;
            }

            return base.Control;
        }
    }

    public FgoAetChildNode(string name, FgoAetChild data, FgoAetSet set, FgoAetRecord owner,
        FgoAetRecord scene = null) : base(name, data)
    {
        mSet = set ?? throw new ArgumentNullException(nameof(set));
        ArgumentNullException.ThrowIfNull(owner);
        mScene = scene ?? owner;
        mIsComposition = set.Records.FirstOrDefault(record => record.Index == (int)data.Kind)?.Type ==
                         FgoAetRecordType.Scene;
    }
}

public sealed class FgoAetSourceNode : Node<FgoAetSource>
{
    public override NodeFlags Flags => NodeFlags.None;

    public override Bitmap Image => ResourceStore.LoadBitmap("Icons/Texture.png");

    private FgoSpriteResolution Resolution =>
        AetResourceContext.Instance.Resolve(Data);

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
    public string ResourceTablePath => Resolution?.Package.TablePath ?? string.Empty;

    [Category("Asset")]
    public int ResourceTableIndex => Resolution?.Entry.Index ?? -1;

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
