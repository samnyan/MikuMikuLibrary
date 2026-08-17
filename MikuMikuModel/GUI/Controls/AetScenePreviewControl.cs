using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;
using MikuMikuLibrary.Aets;
using MikuMikuLibrary.Aets.Resources;
using MikuMikuModel.Resources;

namespace MikuMikuModel.GUI.Controls;

/// <summary>Renders one static frame of an FGO AET scene.</summary>
/// <remarks>
/// Resource lookup, archive I/O, texture decoding, and frame composition all
/// run on the thread pool. The UI thread only updates progress and swaps the
/// completed bitmap into the PictureBox. The bitmap cache is deliberately
/// independent from scene composition so it can be reused by animation.
/// </remarks>
public sealed class AetScenePreviewControl : UserControl
{
    private static AetScenePreviewControl sInstance;

    private readonly Label mStatusLabel = new();
    private readonly ProgressBar mProgressBar = new();
    private readonly PictureBox mPictureBox = new();
    private readonly FgoSpriteBitmapCache mBitmapCache = new();
    private Bitmap mBitmap;
    private CancellationTokenSource mRenderCancellation;
    private int mRenderVersion;
    private bool mDisposed;

    /// <summary>Gets the shared scene preview control instance.</summary>
    public static AetScenePreviewControl Instance => sInstance ??= new();

    /// <summary>Starts rendering a scene at its first static frame.</summary>
    /// <param name="set">The parsed FGO AET set.</param>
    /// <param name="scene">The scene record to render.</param>
    public void SetScene(FgoAetSet set, FgoAetRecord scene)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(scene);

        mRenderCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        mRenderCancellation = cancellation;
        int version = Interlocked.Increment(ref mRenderVersion);

        DisposeBitmap();
        mProgressBar.Visible = false;
        mStatusLabel.Text = string.Empty;

        if (scene.Type != FgoAetRecordType.Scene)
        {
            cancellation.Dispose();
            mRenderCancellation = null;
            SetStatus("Not a scene");
            return;
        }

        mStatusLabel.Text = "Preparing scene...";
        mStatusLabel.ForeColor = Color.DarkOrange;
        var progress = new Progress<SceneRenderProgress>(value =>
        {
            if (version == Volatile.Read(ref mRenderVersion) && !mDisposed)
                UpdateProgress(value);
        });

        _ = RenderAndPresentAsync(set, scene, cancellation, version, progress);
    }

    private async Task RenderAndPresentAsync(FgoAetSet set, FgoAetRecord scene,
        CancellationTokenSource cancellation, int version,
        IProgress<SceneRenderProgress> progress)
    {
        try
        {
            var result = await RenderSceneAsync(set, scene, cancellation.Token, progress);
            if (cancellation.IsCancellationRequested || version != Volatile.Read(ref mRenderVersion) || mDisposed)
            {
                result.Dispose();
                return;
            }

            DisposeBitmap();
            mBitmap = result.DetachBitmap();
            mPictureBox.Image = mBitmap;
            mProgressBar.Visible = false;
            mStatusLabel.Text = result.MissingAssets > 0
                ? $"Static frame 1  •  {result.RenderedAssets} assets  •  {result.MissingAssets} unresolved"
                : $"Static frame 1  •  {result.Width}x{result.Height}";
            mStatusLabel.ForeColor = result.MissingAssets > 0 ? Color.DarkOrange : Color.DarkGreen;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer selection replaced this render. Its result is discarded
            // and the newer render owns the UI.
        }
        catch (Exception)
        {
            if (!cancellation.IsCancellationRequested && version == Volatile.Read(ref mRenderVersion))
                SetStatus("Scene render error");
        }
        finally
        {
            cancellation.Dispose();
            if (ReferenceEquals(mRenderCancellation, cancellation))
                mRenderCancellation = null;
        }
    }

    private async Task<SceneRenderResult> RenderSceneAsync(FgoAetSet set, FgoAetRecord scene,
        CancellationToken cancellationToken, IProgress<SceneRenderProgress> progress)
    {
        var records = set.Records.ToDictionary(record => record.Index);
        var assets = new List<FgoAetRecord>();
        CollectAssets(records, scene, assets, new HashSet<int>());
        progress.Report(new SceneRenderProgress(0, assets.Count, mBitmapCache.AtlasCount));

        var bitmaps = new Dictionary<int, Bitmap>();
        int missingAssets = 0;
        try
        {
            for (int index = 0; index < assets.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var asset = assets[index];
                if (asset.Sources.Count > 0)
                {
                    var source = asset.Sources[0];
                    var resolution = AetResourceContext.Instance.Resolve(source.Path, source.Name);
                    if (resolution == null)
                    {
                        missingAssets++;
                    }
                    else
                    {
                        try
                        {
                            var bitmap = await mBitmapCache.LoadSpriteAsync(resolution, cancellationToken)
                                .ConfigureAwait(false);
                            if (bitmap == null)
                                missingAssets++;
                            else
                                bitmaps[asset.Index] = bitmap;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception)
                        {
                            missingAssets++;
                        }
                    }
                }

                progress.Report(new SceneRenderProgress(index + 1, assets.Count, mBitmapCache.AtlasCount));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = await Task.Run(() => DrawScene(scene, records, bitmaps, missingAssets,
                cancellationToken), cancellationToken).ConfigureAwait(false);
            bitmaps = null;
            return result;
        }
        finally
        {
            if (bitmaps != null)
            {
                foreach (var bitmap in bitmaps.Values)
                    bitmap.Dispose();
            }
        }
    }

    private static void CollectAssets(IReadOnlyDictionary<int, FgoAetRecord> records,
        FgoAetRecord scene, ICollection<FgoAetRecord> assets, ISet<int> visited)
    {
        if (!visited.Add(scene.Index))
            return;

        try
        {
            foreach (var child in scene.Children)
            {
                if (!records.TryGetValue((int)child.Kind, out var record))
                    continue;

                if (record.Type == FgoAetRecordType.Scene)
                    CollectAssets(records, record, assets, visited);
                else if (record.Type == FgoAetRecordType.Asset)
                    assets.Add(record);
            }
        }
        finally
        {
            visited.Remove(scene.Index);
        }
    }

    private static SceneRenderResult DrawScene(FgoAetRecord scene,
        IReadOnlyDictionary<int, FgoAetRecord> records,
        IReadOnlyDictionary<int, Bitmap> bitmaps, int missingAssets,
        CancellationToken cancellationToken)
    {
        int width = scene.Width > 0 && scene.Width <= 8192 ? (int)scene.Width : 1920;
        int height = scene.Height > 0 && scene.Height <= 8192 ? (int)scene.Height : 1080;
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        int renderedAssets = 0;

        try
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(ToColor(scene.Color));
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;

                var state = new RenderState(records, bitmaps, graphics, new HashSet<int>(),
                    cancellationToken, renderedAssets);
                using var identity = new Matrix();
                RenderScene(state, scene, identity, 0);
                renderedAssets = state.RenderedAssets;
            }

            return new SceneRenderResult(bitmap, width, height, renderedAssets, missingAssets);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void RenderScene(RenderState state, FgoAetRecord scene,
        Matrix parentTransform, int depth)
    {
        state.CancellationToken.ThrowIfCancellationRequested();
        if (depth > 64 || !state.Visited.Add(scene.Index))
            return;

        try
        {
            foreach (var child in scene.Children)
            {
                state.CancellationToken.ThrowIfCancellationRequested();
                if (!state.Records.TryGetValue((int)child.Kind, out var record))
                    continue;

                using var transform = parentTransform.Clone();
                ApplyTransform(transform, child);

                if (record.Type == FgoAetRecordType.Scene)
                    RenderScene(state, record, transform, depth + 1);
                else if (record.Type == FgoAetRecordType.Asset)
                    RenderAsset(state, record, transform);
            }
        }
        finally
        {
            state.Visited.Remove(scene.Index);
        }
    }

    private static void RenderAsset(RenderState state, FgoAetRecord asset, Matrix transform)
    {
        if (!state.Bitmaps.TryGetValue(asset.Index, out var bitmap))
            return;

        state.RenderedAssets++;
        using var attributes = new ImageAttributes();
        var color = Math.Clamp(asset.Color >> 24, 0, 255) / 255.0f;
        if (color < 1.0f)
        {
            var matrix = new ColorMatrix { Matrix33 = color };
            attributes.SetColorMatrix(matrix);
        }

        var corners = new[]
        {
            new PointF(0, 0),
            new PointF(bitmap.Width, 0),
            new PointF(0, bitmap.Height)
        };
        transform.TransformPoints(corners);
        state.Graphics.DrawImage(bitmap,
            new[] { corners[0], corners[1], corners[2] },
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            GraphicsUnit.Pixel, attributes);
    }

    private static void ApplyTransform(Matrix transform, FgoAetChild child)
    {
        transform.Translate(child.StaticPositionX, child.StaticPositionY, MatrixOrder.Append);
        var scale = child.StaticScale;
        if (float.IsFinite(scale) && Math.Abs(scale - 1.0f) > 0.0001f)
            transform.Scale(scale, scale, MatrixOrder.Append);
    }

    private static Color ToColor(uint value) => Color.FromArgb(
        (int)((value >> 24) & 0xFF),
        (int)((value >> 16) & 0xFF),
        (int)((value >> 8) & 0xFF),
        (int)(value & 0xFF));

    private void UpdateProgress(SceneRenderProgress value)
    {
        if (value.Total <= 0)
        {
            mProgressBar.Visible = false;
            mStatusLabel.Text = "Rendering scene...";
            return;
        }

        mProgressBar.Visible = true;
        mProgressBar.Style = ProgressBarStyle.Continuous;
        mProgressBar.Maximum = value.Total;
        mProgressBar.Value = Math.Clamp(value.Completed, 0, value.Total);
        mStatusLabel.Text = $"Loading sprites {value.Completed}/{value.Total}  •  " +
                            $"{value.LoadedAtlases} texture package(s)";
        mStatusLabel.ForeColor = Color.DarkOrange;
    }

    private void SetStatus(string status)
    {
        mPictureBox.Image = null;
        mProgressBar.Visible = false;
        mStatusLabel.Text = status;
        mStatusLabel.ForeColor = Color.DarkRed;
    }

    private void DisposeBitmap()
    {
        mPictureBox.Image = null;
        mBitmap?.Dispose();
        mBitmap = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            mDisposed = true;
            Interlocked.Increment(ref mRenderVersion);
            mRenderCancellation?.Cancel();
            DisposeBitmap();
            mBitmapCache.Dispose();
            mProgressBar.Dispose();
            mPictureBox.Dispose();
            mStatusLabel.Dispose();
        }

        base.Dispose(disposing);
    }

    private AetScenePreviewControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        mPictureBox.Dock = DockStyle.Fill;
        mPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        mPictureBox.BackColor = Color.LightGray;

        mProgressBar.Dock = DockStyle.Bottom;
        mProgressBar.Height = 18;
        mProgressBar.Visible = false;

        mStatusLabel.Dock = DockStyle.Bottom;
        mStatusLabel.AutoSize = false;
        mStatusLabel.Height = 24;
        mStatusLabel.Padding = new Padding(6, 3, 6, 3);
        mStatusLabel.Font = new Font(mStatusLabel.Font, FontStyle.Bold);

        Controls.Add(mPictureBox);
        Controls.Add(mProgressBar);
        Controls.Add(mStatusLabel);
        SetStatus("No scene selected");
    }

    private sealed record SceneRenderProgress(int Completed, int Total, int LoadedAtlases);

    private sealed class SceneRenderResult : IDisposable
    {
        private Bitmap mBitmap;

        public int Width { get; }
        public int Height { get; }
        public int RenderedAssets { get; }
        public int MissingAssets { get; }

        public SceneRenderResult(Bitmap bitmap, int width, int height,
            int renderedAssets, int missingAssets)
        {
            mBitmap = bitmap;
            Width = width;
            Height = height;
            RenderedAssets = renderedAssets;
            MissingAssets = missingAssets;
        }

        public Bitmap DetachBitmap()
        {
            var bitmap = mBitmap;
            mBitmap = null;
            return bitmap;
        }

        public void Dispose()
        {
            mBitmap?.Dispose();
            mBitmap = null;
        }
    }

    private sealed class RenderState
    {
        public IReadOnlyDictionary<int, FgoAetRecord> Records { get; }
        public IReadOnlyDictionary<int, Bitmap> Bitmaps { get; }
        public Graphics Graphics { get; }
        public HashSet<int> Visited { get; }
        public CancellationToken CancellationToken { get; }
        public int RenderedAssets { get; set; }

        public RenderState(IReadOnlyDictionary<int, FgoAetRecord> records,
            IReadOnlyDictionary<int, Bitmap> bitmaps, Graphics graphics,
            HashSet<int> visited, CancellationToken cancellationToken,
            int renderedAssets)
        {
            Records = records;
            Bitmaps = bitmaps;
            Graphics = graphics;
            Visited = visited;
            CancellationToken = cancellationToken;
            RenderedAssets = renderedAssets;
        }
    }
}
