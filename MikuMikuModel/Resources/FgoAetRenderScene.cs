using System.Numerics;
using MikuMikuLibrary.Aets;
using MikuMikuLibrary.Aets.Resources;

namespace MikuMikuModel.Resources;

/// <summary>Immutable render topology for one FGO AET composition.</summary>
public sealed class FgoAetRenderScene
{
    /// <summary>Gets the source AET scene.</summary>
    public FgoAetRecord Source { get; }

    /// <summary>Gets the scene width used by the 2D projection.</summary>
    public int Width { get; }

    /// <summary>Gets the scene height used by the 2D projection.</summary>
    public int Height { get; }

    /// <summary>Gets the scene duration in seconds.</summary>
    public float Duration { get; }

    /// <summary>Gets the scene frame rate (frames per second).</summary>
    public float FrameRate { get; }

    /// <summary>Gets the root layer instances in draw order.</summary>
    public IReadOnlyList<FgoAetRenderLayer> Layers { get; }

    /// <summary>
    /// Resolves a layer's local timeline time for a composition time. This is
    /// also used by the Inspector so nested compositions use the same timing
    /// conversion as the renderer.
    /// </summary>
    public bool TryGetLayerTime(FgoAetChild target, float compositionTime, out float localTime)
    {
        ArgumentNullException.ThrowIfNull(target);
        return TryGetLayerTime(Layers, target, compositionTime, out localTime);
    }

    private static bool TryGetLayerTime(
        IEnumerable<FgoAetRenderLayer> layers,
        FgoAetChild target,
        float time,
        out float localTime)
    {
        foreach (var layer in layers)
        {
            if (ReferenceEquals(layer.Data, target))
            {
                localTime = time;
                return true;
            }

            if (layer.IsComposition &&
                TryGetLayerTime(layer.Children, target, layer.Data.ToLocalTime(time), out localTime))
                return true;
        }

        localTime = 0.0f;
        return false;
    }

    private FgoAetRenderScene(FgoAetRecord source, List<FgoAetRenderLayer> layers)
    {
        Source = source;
        Width = source.Width is > 0 and <= 16384 ? (int)source.Width : 1920;
        Height = source.Height is > 0 and <= 16384 ? (int)source.Height : 1080;
        Duration = source.Duration;
        FrameRate = source.FrameRate;
        Layers = layers;
    }

    /// <summary>Builds a render topology from the parsed AET records.</summary>
    /// <param name="set">The parsed AET set.</param>
    /// <param name="scene">The scene record to render.</param>
    /// <returns>A scene topology with resolved Sprite references.</returns>
    public static FgoAetRenderScene Build(FgoAetSet set, FgoAetRecord scene)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.Type != FgoAetRecordType.Scene)
            throw new ArgumentException("The selected AET record is not a scene.", nameof(scene));

        var records = set.Records.ToDictionary(record => record.Index);
        var layers = BuildLayers(records, scene, new HashSet<int>());
        return new FgoAetRenderScene(scene, layers);
    }

    private static List<FgoAetRenderLayer> BuildLayers(
        IReadOnlyDictionary<int, FgoAetRecord> records,
        FgoAetRecord scene,
        ISet<int> activeScenes)
    {
        var result = new List<FgoAetRenderLayer>(scene.Children.Count);
        if (!activeScenes.Add(scene.Index))
            return result;

        try
        {
            foreach (var child in scene.Children)
            {
                records.TryGetValue((int)child.Kind, out var target);
                var layer = new FgoAetRenderLayer(child, target);
                if (target?.Type == FgoAetRecordType.Scene)
                    layer.Children.AddRange(BuildLayers(records, target, activeScenes));
                else if (target?.Type == FgoAetRecordType.Asset && target.Sources.Count > 0)
                {
                    // The Asset source is authoritative. A layer label may be
                    // duplicated across Sprite tables, while the source name
                    // retains the package-specific prefix used for selection.
                    var source = target.Sources[0];
                    layer.Resolution = AetResourceContext.Instance.Resolve(source);
                }

                result.Add(layer);
            }

            // Keep the raw parent slot available for inspection.  Current
            // FGO Arcade exports use -1 here; actual composition nesting is
            // expressed by the child Kind -> Scene record link and is handled
            // by the recursive traversal above.  Do not derive a parent from
            // Value1/offset +8: that field contains exporter flags.
            var layersByIndex = result.ToDictionary(layer => layer.Data.Index);
            foreach (var layer in result)
            {
                if (layersByIndex.TryGetValue(layer.Data.ParentIndex, out var parent) &&
                    !ReferenceEquals(parent, layer))
                    layer.Parent = parent;
            }
        }
        finally
        {
            activeScenes.Remove(scene.Index);
        }

        return result;
    }
}
/// <summary>One composition layer with stable topology and mutable visibility.</summary>
public sealed class FgoAetRenderLayer
{
    /// <summary>Gets the parsed layer data.</summary>
    public FgoAetChild Data { get; }

    /// <summary>Gets the linked Scene or Asset record, if any.</summary>
    public FgoAetRecord Target { get; }

    /// <summary>Gets the resolved Sprite resource for an Asset layer.</summary>
    public FgoSpriteResolution Resolution { get; internal set; }

    /// <summary>Gets child layers for a linked composition.</summary>
    public List<FgoAetRenderLayer> Children { get; } = new();

    /// <summary>Gets the FGO AE parent layer, if one is assigned.</summary>
    public FgoAetRenderLayer Parent { get; internal set; }

    /// <summary>Gets whether the layer points to a linked composition.</summary>
    public bool IsComposition => Target?.Type == FgoAetRecordType.Scene;

    /// <summary>Gets whether the layer points to a Sprite asset.</summary>
    public bool IsAsset => Target?.Type == FgoAetRecordType.Asset;

    /// <summary>
    /// Gets the logical sprite dimensions carried by the AET asset record.
    /// FGO's final sprite transform keeps these dimensions separate from the
    /// atlas crop rectangle; the latter is only used for UV coordinates.
    /// </summary>
    public (int Width, int Height) LogicalSpriteSize =>
        IsAsset && Target.Width is > 0 and <= ushort.MaxValue &&
        Target.Height is > 0 and <= ushort.MaxValue
            ? ((int)Target.Width, (int)Target.Height)
            : (0, 0);

    /// <summary>Gets the layer's current visibility state.</summary>
    public bool Visible
    {
        get => Data.IsVisible;
        set => Data.IsVisible = value;
    }

    /// <summary>Evaluates the layer transform at a composition time in seconds.</summary>
    public Matrix4x4 EvaluateTransform(
        float time,
        float duration,
        float inheritedOpacity,
        out float opacity)
    {
        float scaleX = NormalizeScale(Data.EvaluateScaleX(time, duration));
        float scaleY = NormalizeScale(Data.EvaluateScaleY(time, duration));
        float scaleZ = NormalizeScale(Data.EvaluateScaleZ(time, duration));
        float rotationX = Data.EvaluateRotationX(time, duration);
        float rotationY = Data.EvaluateRotationY(time, duration);
        float rotationZ = Data.EvaluateRotation(time, duration);
        float orientationX = Data.EvaluateOrientationX(time, duration);
        float orientationY = Data.EvaluateOrientationY(time, duration);
        float orientationZ = Data.EvaluateOrientationZ(time, duration);
        float anchorX = Data.EvaluateAnchorX(time, duration);
        float anchorY = Data.EvaluateAnchorY(time, duration);
        float anchorZ = Data.EvaluateAnchorZ(time, duration);
        float positionX = Data.EvaluatePositionX(time, duration);
        float positionY = Data.EvaluatePositionY(time, duration);
        float positionZ = -Data.EvaluatePositionZ(time, duration);

        // Native AetTransform_Calculate2D builds the linear part in rotation
        // order, then scales its X/Y/Z axes, and finally applies the negative
        // anchor through that already-scaled/rotated matrix before adding
        // position. System.Numerics uses row-vector composition (and the GL
        // upload preserves that convention), so this is T(-anchor) * R * S *
        // T(position), not T(-anchor) * S * R * T(position).
        var transform = Matrix4x4.CreateTranslation(-anchorX, -anchorY, -anchorZ) *
                        Matrix4x4.CreateRotationX(ToRadians(orientationX)) *
                        Matrix4x4.CreateRotationY(ToRadians(-orientationY)) *
                        Matrix4x4.CreateRotationZ(ToRadians(orientationZ)) *
                        Matrix4x4.CreateRotationX(ToRadians(-rotationX)) *
                        Matrix4x4.CreateRotationY(ToRadians(-rotationY)) *
                        Matrix4x4.CreateRotationZ(ToRadians(rotationZ)) *
                        Matrix4x4.CreateScale(scaleX, scaleY, scaleZ) *
                        Matrix4x4.CreateTranslation(positionX, positionY, positionZ);

        opacity = inheritedOpacity * NormalizeOpacity(Data.EvaluateOpacity(time, duration));
        return transform;
    }

    /// <summary>Creates a render layer for one parsed child record.</summary>
    public FgoAetRenderLayer(FgoAetChild data, FgoAetRecord target)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Target = target;
    }

    public static float NormalizeScale(float value) =>
        float.IsFinite(value) ? value : 1.0f;

    public static float NormalizeOpacity(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0.0f, 1.0f) : 1.0f;

    private static float ToRadians(float degrees) =>
        float.IsFinite(degrees) ? degrees * (MathF.PI / 180.0f) : 0.0f;

}
