using System.Numerics;
using System.Threading;
using System.ComponentModel;
using MikuMikuLibrary.Aets;
using MikuMikuLibrary.Aets.Resources;
using MikuMikuModel.GUI.Controls.ModelView;
using MikuMikuModel.Resources;
using OpenTK.Graphics.OpenGL;
using OpenTK.WinForms;

namespace MikuMikuModel.GUI.Controls;

/// <summary>Hosts the GPU-backed FGO AET scene preview and its timeline.</summary>
public sealed class AetScenePreviewControl : UserControl
{
    private static AetScenePreviewControl sInstance;

    private readonly AetSceneGLView mView = new();
    private readonly Label mStatusLabel = new();
    private readonly ProgressBar mProgressBar = new();
    private readonly Panel mTimelinePanel = new();
    private readonly Button mPlayButton = new();
    private readonly TrackBar mFrameTrackBar = new();
    private readonly Label mFrameLabel = new();
    private readonly CheckBox mShowCanvasBorder = new();
    private readonly FgoSpriteBitmapCache mBitmapCache = new();
    private readonly System.Windows.Forms.Timer mPlaybackTimer = new();
    private CancellationTokenSource mPrepareCancellation;
    private FgoAetSet mSet;
    private FgoAetRecord mScene;
    private FgoAetRenderScene mRenderScene;
    private int mDuration;

    private float mFrameRate = 60.0f;

    // AET curves use seconds as their time coordinate. The track bar stores
    // integer frame numbers and is converted at the boundary.
    private float mCurrentTime;
    private bool mPlaying;
    private bool mDisposed;

    /// <summary>Gets the shared AET preview instance.</summary>
    public static AetScenePreviewControl Instance => sInstance ??= new();

    /// <summary>Gets the preview instance if it has already been created.</summary>
    public static AetScenePreviewControl ExistingInstance => sInstance;

    /// <summary>Gets the current composition time in seconds.</summary>
    public float CurrentTime => mCurrentTime;

    /// <summary>Gets the current composition frame number.</summary>
    public float CurrentFrameNumber => mCurrentTime * mFrameRate;

    /// <summary>Gets the active scene duration in seconds.</summary>
    public float SceneDuration => mRenderScene?.Duration ?? 0.0f;

    /// <summary>Gets the active scene frame rate.</summary>
    public float SceneFrameRate => mFrameRate;

    /// <summary>Raised when the selected AET frame changes.</summary>
    public event EventHandler FrameChanged;

    /// <summary>Resolves a layer's local time in the currently displayed scene.</summary>
    public bool TryGetLayerTime(FgoAetChild layer, out float localTime)
    {
        if (mRenderScene == null)
        {
            localTime = 0.0f;
            return false;
        }

        return mRenderScene.TryGetLayerTime(layer, mCurrentTime, out localTime);
    }

    /// <summary>Requests a redraw after a layer visibility change.</summary>
    public void RefreshVisibility() => mView.Invalidate();

    /// <summary>Selects a layer in the active scene without rebuilding the scene.</summary>
    /// <param name="layer">The parsed AET layer to highlight, or <see langword="null"/> to clear selection.</param>
    public void SelectLayer(FgoAetChild layer)
    {
        if (mRenderScene == null)
            return;

        mView.SelectLayer(layer);
    }

    /// <summary>Shows a scene and selects one of its layers without resetting an already loaded scene.</summary>
    /// <param name="set">The parsed AET set.</param>
    /// <param name="scene">The scene record.</param>
    /// <param name="layer">The tree-selected layer, or <see langword="null"/>.</param>
    public void ShowScene(FgoAetSet set, FgoAetRecord scene, FgoAetChild layer)
    {
        if (!ReferenceEquals(mSet, set) || !ReferenceEquals(mScene, scene) || mRenderScene == null)
            SetScene(set, scene);

        SelectLayer(layer);
    }

    /// <summary>Sets the scene rendered by the control.</summary>
    public void SetScene(FgoAetSet set, FgoAetRecord scene)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(scene);

        if (scene.Type != FgoAetRecordType.Scene)
        {
            mStatusLabel.Text = "Not a scene";
            return;
        }

        mSet = set;
        mScene = scene;
        mRenderScene = FgoAetRenderScene.Build(set, scene);
        mFrameRate = Math.Clamp(mRenderScene.FrameRate, 1.0f, 240.0f);
        mDuration = Math.Clamp((int)MathF.Ceiling(mRenderScene.Duration * mFrameRate), 1, 100000);
        mCurrentTime = 0.0f;
        mPlaying = false;
        mPlayButton.Text = "Play";
        mFrameTrackBar.Maximum = mDuration;
        mFrameTrackBar.Value = 0;
        mPlaybackTimer.Interval = Math.Clamp((int)MathF.Round(1000.0f / mFrameRate), 1, 1000);
        UpdateFrameLabel();
        mView.SetScene(mRenderScene);
        FrameChanged?.Invoke(this, EventArgs.Empty);
        PrepareResourcesAsync(mRenderScene);
    }

    /// <summary>Prepares all referenced atlases off the UI thread.</summary>
    private async void PrepareResourcesAsync(FgoAetRenderScene scene)
    {
        mPrepareCancellation?.Cancel();
        mPrepareCancellation?.Dispose();
        var cancellation = mPrepareCancellation = new CancellationTokenSource();
        var resources = CollectResources(scene).ToArray();
        mProgressBar.Visible = resources.Length > 0;
        mProgressBar.Maximum = Math.Max(1, resources.Length);
        mProgressBar.Value = 0;
        mStatusLabel.Text = resources.Length == 0 ? "No Sprite resources" : "Loading AET resources...";

        try
        {
            var prepared = new Dictionary<string, PreparedAetAtlas>(StringComparer.Ordinal);
            for (int i = 0; i < resources.Length; i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var resolution = resources[i];
                try
                {
                    var atlas = await mBitmapCache.LoadAtlasAsync(resolution, cancellation.Token)
                        .ConfigureAwait(false);
                    prepared[CreateResourceKey(resolution)] =
                        new PreparedAetAtlas(resolution, atlas);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"AET resource load failed for '{resolution.Entry.Name}': {exception.Message}");
                }

                int completed = i + 1;
                PostToUi(() =>
                {
                    if (!mDisposed && ReferenceEquals(mRenderScene, scene))
                    {
                        mProgressBar.Value = Math.Clamp(completed, 0, mProgressBar.Maximum);
                        mStatusLabel.Text = $"Loading AET resources {completed}/{resources.Length}";
                    }
                });
            }

            cancellation.Token.ThrowIfCancellationRequested();
            PostToUi(() =>
            {
                if (mDisposed || !ReferenceEquals(mRenderScene, scene))
                    return;

                mView.SetResources(prepared);
                mProgressBar.Visible = false;
                mStatusLabel.Text = $"Frame {CurrentFrameNumber:0.##}/{mDuration} · GPU atlases {prepared.Count}";
                mView.Invalidate();
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PostToUi(() =>
            {
                if (!mDisposed && ReferenceEquals(mRenderScene, scene))
                    SetStatus($"AET resource error: {exception.Message}");
            });
        }
    }

    private static IEnumerable<FgoSpriteResolution> CollectResources(FgoAetRenderScene scene)
    {
        var resources = new Dictionary<string, FgoSpriteResolution>(StringComparer.Ordinal);

        void Visit(IEnumerable<FgoAetRenderLayer> layers)
        {
            foreach (var layer in layers)
            {
                if (layer.Resolution != null)
                    resources.TryAdd(CreateResourceKey(layer.Resolution), layer.Resolution);
                Visit(layer.Children);
            }
        }

        Visit(scene.Layers);
        return resources.Values;
    }

    private static string CreateResourceKey(FgoSpriteResolution resolution) => string.Join("\u001f",
        resolution.Package.ArchivePath ?? resolution.Package.TablePath,
        resolution.Entry.TextureIndex, resolution.Entry.TextureName,
        resolution.Entry.AtlasWidth, resolution.Entry.AtlasHeight);

    private void TogglePlayback()
    {
        if (mRenderScene == null)
            return;

        mPlaying = !mPlaying;
        mPlayButton.Text = mPlaying ? "Pause" : "Play";
        if (mPlaying)
            mPlaybackTimer.Start();
        else
            mPlaybackTimer.Stop();
    }

    private void AdvanceFrame()
    {
        if (!mPlaying || mRenderScene == null)
            return;

        mCurrentTime += 1.0f / mFrameRate;
        if (mCurrentTime > mRenderScene.Duration)
            mCurrentTime = 0.0f;
        int frame = Math.Clamp((int)MathF.Round(mCurrentTime * mFrameRate), 0, mDuration);
        bool frameAlreadySelected = mFrameTrackBar.Value == frame;
        mFrameTrackBar.Value = frame;
        // ValueChanged handles the common case. If rounding keeps the same
        // trackbar value, still update the renderer and Inspector for the
        // fractional time that advanced between two timer ticks.
        if (frameAlreadySelected)
        {
            UpdateFrameLabel();
            mView.SetFrame(mCurrentTime);
            FrameChanged?.Invoke(this, EventArgs.Empty);
        }
        mStatusLabel.Text = $"Frame {CurrentFrameNumber:0.##}/{mDuration}";
    }

    private void OnFrameChanged()
    {
        if (mRenderScene == null)
            return;

        mCurrentTime = mFrameTrackBar.Value / mFrameRate;
        UpdateFrameLabel();
        mView.SetFrame(mCurrentTime);
        FrameChanged?.Invoke(this, EventArgs.Empty);
        if (!mPlaying)
            mStatusLabel.Text = $"Frame {CurrentFrameNumber:0.##}/{mDuration}";
    }

    private void UpdateFrameLabel() => mFrameLabel.Text = $"Frame {CurrentFrameNumber:0.##}/{mDuration}";

    private void SetStatus(string status)
    {
        mProgressBar.Visible = false;
        mStatusLabel.Text = status;
        mStatusLabel.ForeColor = Color.DarkRed;
    }

    /// <summary>Posts a UI update while tolerating a handle recreation during resize.</summary>
    private void PostToUi(Action action)
    {
        if (mDisposed || IsDisposed || Disposing || !IsHandleCreated)
            return;

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // WinForms can destroy/recreate the handle between the checks above
            // and BeginInvoke while the user is resizing the main window.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            mDisposed = true;
            mPrepareCancellation?.Cancel();
            mPrepareCancellation?.Dispose();
            mPlaybackTimer.Stop();
            mPlaybackTimer.Dispose();
            mBitmapCache.Dispose();
            mView.Dispose();
            mProgressBar.Dispose();
            mTimelinePanel.Dispose();
            mPlayButton.Dispose();
            mFrameTrackBar.Dispose();
            mFrameLabel.Dispose();
            mShowCanvasBorder.Dispose();
            mStatusLabel.Dispose();
        }

        base.Dispose(disposing);
    }

    private AetScenePreviewControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        mView.Dock = DockStyle.Fill;
        mView.FrameRendered += OnFrameRendered;
        mView.RenderUnavailable += (sender, reason) => SetStatus(reason);
        mView.LayerSelected += (sender, name) =>
        {
            if (!string.IsNullOrEmpty(name))
                mStatusLabel.Text = $"Selected: {name}  •  Frame {CurrentFrameNumber:0.##}/{mDuration}";
        };

        mProgressBar.Dock = DockStyle.Bottom;
        mProgressBar.Height = 18;
        mProgressBar.Visible = false;

        mStatusLabel.Dock = DockStyle.Bottom;
        mStatusLabel.AutoSize = false;
        mStatusLabel.Height = 24;
        mStatusLabel.Padding = new Padding(6, 3, 6, 3);
        mStatusLabel.Font = new Font(mStatusLabel.Font, FontStyle.Bold);

        mPlayButton.Text = "Play";
        mPlayButton.Width = 64;
        mPlayButton.Dock = DockStyle.Left;
        mPlayButton.Click += (sender, args) => TogglePlayback();

        mFrameLabel.AutoSize = false;
        mFrameLabel.Width = 110;
        mFrameLabel.TextAlign = ContentAlignment.MiddleRight;
        mFrameLabel.Dock = DockStyle.Right;

        mShowCanvasBorder.AutoSize = false;
        mShowCanvasBorder.Width = 145;
        mShowCanvasBorder.Text = "Show canvas border";
        mShowCanvasBorder.TextAlign = ContentAlignment.MiddleLeft;
        mShowCanvasBorder.Dock = DockStyle.Right;
        mShowCanvasBorder.CheckedChanged += (sender, args) =>
            mView.ShowCanvasBorder = mShowCanvasBorder.Checked;

        mFrameTrackBar.Minimum = 0;
        mFrameTrackBar.Maximum = 1;
        mFrameTrackBar.TickStyle = TickStyle.None;
        mFrameTrackBar.Dock = DockStyle.Fill;
        mFrameTrackBar.ValueChanged += (sender, args) => OnFrameChanged();

        mTimelinePanel.Dock = DockStyle.Bottom;
        mTimelinePanel.Height = 34;
        mTimelinePanel.Padding = new Padding(4, 0, 4, 0);
        mTimelinePanel.Controls.Add(mFrameTrackBar);
        mTimelinePanel.Controls.Add(mFrameLabel);
        mTimelinePanel.Controls.Add(mShowCanvasBorder);
        mTimelinePanel.Controls.Add(mPlayButton);

        mPlaybackTimer.Interval = 33;
        mPlaybackTimer.Tick += (sender, args) => AdvanceFrame();

        Controls.Add(mView);
        Controls.Add(mProgressBar);
        Controls.Add(mStatusLabel);
        Controls.Add(mTimelinePanel);
        SetStatus("No scene selected");
    }

    private void OnFrameRendered(object sender, AetFrameRenderEventArgs args)
    {
        if (!mDisposed && mRenderScene != null && args.Scene == mRenderScene)
        {
            mStatusLabel.ForeColor = args.MissingAssets > 0 ? Color.DarkOrange : Color.DarkGreen;
            if (!mPlaying)
                mStatusLabel.Text = $"Frame {CurrentFrameNumber:0.##}/{mDuration} · " +
                                    $"sprites {args.RenderedAssets}, missing {args.MissingAssets}";
        }
    }

    private sealed record PreparedAetAtlas(FgoSpriteResolution Resolution, Bitmap Atlas);

    private sealed class AetFrameRenderEventArgs : EventArgs
    {
        public FgoAetRenderScene Scene { get; }
        public int MissingAssets { get; }
        public int RenderedAssets { get; }

        public AetFrameRenderEventArgs(
            FgoAetRenderScene scene,
            int missingAssets,
            int renderedAssets)
        {
            Scene = scene;
            MissingAssets = missingAssets;
            RenderedAssets = renderedAssets;
        }
    }

    private sealed class AetSceneGLView : GLControl
    {
        private readonly Dictionary<string, GpuAetAtlas> mAtlases = new(StringComparer.Ordinal);
        private readonly List<HitLayer> mHitLayers = new();
        private GLShaderProgram mShader;
        private GLShaderProgram mCanvasShader;
        private GLBuffer<float> mVertexBuffer;
        private GLBuffer<uint> mIndexBuffer;
        private GLBuffer<float> mCanvasVertexBuffer;
        private int mVertexArray;
        private int mCanvasVertexArray;
        private FgoAetRenderScene mScene;
        private float mTime;
        private float mZoom = 1.0f;
        private Vector2 mPan;
        private Point mPreviousMouse;
        private bool mPanning;
        private HitLayer mSelectedLayer;
        private FgoAetChild mSelectedData;
        private bool mLoaded;
        private bool mDisposing;
        private Dictionary<string, PreparedAetAtlas> mPendingResources;
        private bool mShowCanvasBorder;

        public event EventHandler<AetFrameRenderEventArgs> FrameRendered;
        public event EventHandler<string> LayerSelected;
        public event EventHandler<string> RenderUnavailable;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowCanvasBorder
        {
            get => mShowCanvasBorder;
            set
            {
                if (mShowCanvasBorder == value)
                    return;

                mShowCanvasBorder = value;
                Invalidate();
            }
        }

        public AetSceneGLView() : base(new GLControlSettings { NumberOfSamples = 2 })
        {
            BackColor = Color.LightGray;
        }

        public void SetScene(FgoAetRenderScene scene)
        {
            if (mDisposing || IsDisposed)
                return;

            mScene = scene;
            mTime = 0.0f;
            mHitLayers.Clear();
            mSelectedLayer = null;
            mSelectedData = null;
            mZoom = 1.0f;
            mPan = Vector2.Zero;
            Invalidate();
        }

        public void SetFrame(float time)
        {
            if (mDisposing || IsDisposed)
                return;

            mTime = time;
            Invalidate();
        }

        public void SetResources(Dictionary<string, PreparedAetAtlas> resources)
        {
            if (mDisposing || IsDisposed)
                return;

            mPendingResources = resources;
            if (mLoaded)
                UploadResources();
            Invalidate();
        }

        public void SelectLayer(FgoAetChild layer)
        {
            if (mDisposing || IsDisposed)
                return;

            mSelectedData = layer;
            mSelectedLayer = null;
            Invalidate();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!TryMakeCurrent())
            {
                RenderUnavailable?.Invoke(this, "OpenGL AET renderer is unavailable");
                return;
            }

            // Create the program only after GLControl has created its native
            // child window. Constructing it in the WinForms constructor can
            // run before a valid context exists.
            mShader = GLShaderProgram.Create("Aet2D");
            mCanvasShader = GLShaderProgram.Create("AetCanvas");
            mLoaded = mShader != null;
            if (!mLoaded)
            {
                RenderUnavailable?.Invoke(this, "OpenGL AET renderer is unavailable");
                return;
            }
            mVertexArray = GL.GenVertexArray();
            GL.BindVertexArray(mVertexArray);
            mVertexBuffer = new GLBuffer<float>(BufferTarget.ArrayBuffer,
                new[]
                {
                    0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f,
                    1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f, 0.0f, 1.0f
                },
                BufferUsageHint.StaticDraw);
            mIndexBuffer = new GLBuffer<uint>(BufferTarget.ElementArrayBuffer,
                new[] { 0u, 1u, 2u, 0u, 2u, 3u }, BufferUsageHint.StaticDraw);
            mVertexBuffer.Bind();
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            mIndexBuffer.Bind();

            // The border is drawn in the same scene coordinate space as the
            // sprites, so it remains aligned while the view is fitted, zoomed
            // or panned.  A separate VAO/shader keeps the textured sprite
            // vertex state untouched.
            if (mCanvasShader != null)
            {
                mCanvasVertexArray = GL.GenVertexArray();
                GL.BindVertexArray(mCanvasVertexArray);
                mCanvasVertexBuffer = new GLBuffer<float>(BufferTarget.ArrayBuffer,
                    new[] { 0.0f, 0.0f, 1.0f, 0.0f, 1.0f, 1.0f, 0.0f, 1.0f },
                    BufferUsageHint.StaticDraw);
                mCanvasVertexBuffer.Bind();
                GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false,
                    2 * sizeof(float), 0);
                GL.EnableVertexAttribArray(0);
            }

            GL.BindVertexArray(mVertexArray);
            UploadResources();
        }

        private void UploadResources()
        {
            if (!mLoaded || mPendingResources == null)
                return;

            if (!TryMakeCurrent())
                return;

            var pending = mPendingResources;
            var uploaded = new Dictionary<string, GpuAetAtlas>(StringComparer.Ordinal);
            try
            {
                foreach (var pair in pending)
                    uploaded[pair.Key] = new GpuAetAtlas(new GLTexture(pair.Value.Atlas),
                        pair.Value.Atlas.Width, pair.Value.Atlas.Height);

                foreach (var atlas in mAtlases.Values)
                    atlas.Texture.Dispose();
                mAtlases.Clear();
                foreach (var pair in uploaded)
                    mAtlases[pair.Key] = pair.Value;
                mPendingResources = null;
            }
            catch (Exception exception)
            {
                foreach (var sprite in uploaded.Values)
                {
                    try
                    {
                        sprite.Texture.Dispose();
                    }
                    catch (Exception disposeException)
                    {
                        Debug.WriteLine($"AET texture cleanup failed: {disposeException.Message}");
                    }
                }

                Debug.WriteLine($"AET texture upload failed: {exception}");
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // GLControl.OnPaint calls EnsureCreated(), which guarantees that
            // its child GLFW window/context exists before issuing GL commands.
            // Keep the base call even though the actual frame is rendered by
            // this override.
            try
            {
                base.OnPaint(e);
            }
            catch (InvalidOperationException exception)
            {
                Debug.WriteLine($"AET GLControl paint setup failed: {exception.Message}");
                return;
            }

            if (!mLoaded || mShader == null || mDisposing || IsDisposed ||
                ClientSize.Width <= 0 || ClientSize.Height <= 0 || !TryMakeCurrent())
            {
                e.Graphics.Clear(Color.LightGray);
                return;
            }

            // Resource completion can coincide with a handle resize. Retry a
            // deferred upload on the next paint once the context is current.
            UploadResources();

            try
            {
                GL.Viewport(0, 0, ClientSize.Width, ClientSize.Height);
                var background = mScene == null ? Color.LightGray : ToColor(mScene.Source.Color);
                // An uninitialised AET background should not look like a failed
                // renderer. Preserve explicit scene colors, including opaque
                // black, while showing a neutral canvas for transparent colors.
                if (mScene == null || background.A == 0)
                    background = Color.LightGray;
                GL.ClearColor(background.R / 255.0f, background.G / 255.0f,
                    background.B / 255.0f, background.A / 255.0f);
                GL.Clear(ClearBufferMask.ColorBufferBit);
                // Layer and animation values stay in the AET's native coordinate
                // system. The view transform maps that fixed scene canvas into
                // the current window, while this final projection maps window
                // pixels to NDC. Keeping these two spaces separate is important:
                // applying a window-space view to a scene-space projection clips
                // every primitive.
                int sceneWidth = mScene?.Width is > 0 ? mScene.Width : 1920;
                int sceneHeight = mScene?.Height is > 0 ? mScene.Height : 1080;
                var projection = Matrix4x4.CreateOrthographicOffCenter(0, ClientSize.Width,
                    ClientSize.Height, 0, -1, 1);
                float fit = MathF.Min(ClientSize.Width / (float)sceneWidth,
                    ClientSize.Height / (float)sceneHeight) * mZoom;
                var world = Matrix4x4.CreateScale(fit, fit, 1.0f) *
                            Matrix4x4.CreateTranslation(
                                (ClientSize.Width - sceneWidth * fit) * 0.5f + mPan.X,
                                (ClientSize.Height - sceneHeight * fit) * 0.5f + mPan.Y, 0.0f);

                mShader.Use();
                mShader.SetUniform("uProjection", projection);
                GL.ActiveTexture(TextureUnit.Texture0);
                mShader.SetUniform("uTexture", 0);
                GL.BindVertexArray(mVertexArray);
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                if (mScene == null)
                {
                    GL.Disable(EnableCap.Blend);
                    SwapBuffers();
                    return;
                }

                mHitLayers.Clear();
                int missing = 0;
                var renderItems = new List<PreparedLayer>();
                CollectLayers(mScene.Layers, Matrix4x4.Identity, 1.0f, mTime,
                    mScene.Duration, ref missing, false, renderItems);
                // Native stores the flattened AET instances in reverse child
                // index order: the last record in the file (the GameOver
                // background) is runtime index 0 and is drawn first.  Walk
                // the flattened source-order list backwards so backgrounds
                // stay behind text/effects.
                for (int i = renderItems.Count - 1; i >= 0; i--)
                {
                    RenderLayer(renderItems[i], world);
                }
                if (mShowCanvasBorder)
                    RenderCanvasBorder(world, sceneWidth, sceneHeight);
                GL.Disable(EnableCap.Blend);
                SwapBuffers();
                FrameRendered?.Invoke(this, new AetFrameRenderEventArgs(mScene, missing,
                    mHitLayers.Count));
            }
            catch (InvalidOperationException exception)
            {
                // During a WinForms resize the native child window can be
                // temporarily unavailable. Skip that frame and let the next
                // paint recreate the viewport instead of using a stale context.
                Debug.WriteLine($"AET GL frame skipped: {exception.Message}");
            }
            catch (Exception exception)
            {
                // Keep a transient driver/context failure from escaping the
                // WinForms paint callback and terminating the application.
                Debug.WriteLine($"AET GL frame failed: {exception}");
            }
        }

        private void CollectLayers(
            IEnumerable<FgoAetRenderLayer> layers,
            Matrix4x4 parent,
            float inheritedOpacity,
            float time,
            float compositionDuration,
            ref int missing,
            bool highlightDescendants,
            ICollection<PreparedLayer> output)
        {
            foreach (var layer in layers)
            {
                if (!layer.Visible || !layer.Data.IsActive(time))
                    continue;

                var local = layer.EvaluateTransform(time, compositionDuration, inheritedOpacity,
                    out float opacity);
                // FGO keeps AE's ParentIndex relationship separately from a
                // nested Composition link. Resolve the complete parent chain
                // at the same local composition time before applying the
                // current layer transform. Parent opacity is intentionally not
                // inherited here; the game combines the parent matrix and
                // opacity through separate paths.
                var inheritedTransform = EvaluateParentTransform(layer, parent, time,
                    compositionDuration);
                // Native stores column-vector affine matrices and computes
                // parent * local. System.Numerics composes row vectors, so the
                // equivalent order is local * parent.
                var transform = local * inheritedTransform;
                if (layer.IsComposition)
                {
                    float childDuration = layer.Target?.Duration is > 0.0f
                        ? layer.Target.Duration
                        : compositionDuration;
                    CollectLayers(layer.Children, transform, opacity,
                        layer.Data.ToLocalTime(time), childDuration, ref missing,
                        highlightDescendants || ReferenceEquals(mSelectedData, layer.Data), output);
                }
                else if (layer.IsAsset && layer.Resolution != null)
                {
                    string key = CreateResourceKey(layer.Resolution);
                    if (!mAtlases.TryGetValue(key, out var atlas))
                    {
                        missing++;
                        continue;
                    }

                    var rectangle = FgoSpriteBitmap.GetCropRectangle(layer.Resolution.Entry,
                        atlas.Width, atlas.Height);
                    if (rectangle.Width <= 0 || rectangle.Height <= 0)
                    {
                        missing++;
                        continue;
                    }

                    var logicalSize = layer.LogicalSpriteSize;
                    int spriteWidth = logicalSize.Width > 0
                        ? logicalSize.Width
                        : rectangle.Width;
                    int spriteHeight = logicalSize.Height > 0
                        ? logicalSize.Height
                        : rectangle.Height;

                    output.Add(new PreparedLayer(layer, transform, opacity,
                        highlightDescendants || ReferenceEquals(mSelectedData, layer.Data),
                        spriteWidth, spriteHeight));
                }
            }
        }

        private Matrix4x4 EvaluateParentTransform(
            FgoAetRenderLayer layer,
            Matrix4x4 compositionParent,
            float time,
            float compositionDuration)
        {
            return EvaluateParentTransform(layer, compositionParent, time, compositionDuration,
                new HashSet<FgoAetRenderLayer>());
        }

        private Matrix4x4 EvaluateParentTransform(
            FgoAetRenderLayer layer,
            Matrix4x4 compositionParent,
            float time,
            float compositionDuration,
            ISet<FgoAetRenderLayer> visiting)
        {
            var parentLayer = layer.Parent;
            if (parentLayer == null || !visiting.Add(layer))
                return compositionParent;

            try
            {
                var parentTransform = parentLayer.EvaluateTransform(time,
                    compositionDuration, 1.0f, out _);
                var inherited = EvaluateParentTransform(parentLayer,
                    compositionParent, time, compositionDuration, visiting);
                return parentTransform * inherited;
            }
            finally
            {
                visiting.Remove(layer);
            }
        }

        private void RenderLayer(PreparedLayer item, Matrix4x4 world)
        {
            string key = CreateResourceKey(item.Layer.Resolution);
            if (!mAtlases.TryGetValue(key, out var atlas))
                return;

            var rectangle = FgoSpriteBitmap.GetCropRectangle(item.Layer.Resolution.Entry,
                atlas.Width, atlas.Height);
            var uvRect = FgoSpriteBitmap.GetUvRectangle(rectangle, atlas.Width, atlas.Height);
            var size = Matrix4x4.CreateScale(item.Width, item.Height, 1.0f);
            var transform = size * item.Transform * world;
            mShader.SetUniform("uTransform", transform);
            mShader.SetUniform("uUvRect", uvRect);
            mShader.SetUniform("uUvRotated", item.Layer.Resolution.Entry.X0 >
                                             item.Layer.Resolution.Entry.X1);
            float alpha = Math.Clamp(item.Opacity *
                ((item.Layer.Target.Color >> 24) & 0xFF) / 255.0f, 0, 1);
            mShader.SetUniform("uColor", item.Highlight
                ? new Vector4(1.0f, 0.85f, 0.35f, alpha)
                : new Vector4(1, 1, 1, alpha));
            atlas.Texture.Bind();
            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
            mHitLayers.Add(new HitLayer(item.Layer, transform,
                item.Width, item.Height));
        }

        private void RenderCanvasBorder(Matrix4x4 world, int sceneWidth, int sceneHeight)
        {
            if (mCanvasShader == null || mCanvasVertexArray == 0)
                return;

            mCanvasShader.Use();
            mCanvasShader.SetUniform("uProjection", Matrix4x4.CreateOrthographicOffCenter(
                0, ClientSize.Width, ClientSize.Height, 0, -1, 1));
            mCanvasShader.SetUniform("uTransform",
                Matrix4x4.CreateScale(sceneWidth, sceneHeight, 1.0f) * world);
            mCanvasShader.SetUniform("uColor", new Vector4(0.15f, 0.75f, 1.0f, 0.95f));
            GL.BindVertexArray(mCanvasVertexArray);
            GL.LineWidth(2.0f);
            GL.DrawArrays(PrimitiveType.LineLoop, 0, 4);
            GL.BindVertexArray(mVertexArray);
        }

        private HitLayer HitTest(Point point)
        {
            var scenePoint = new Vector2(point.X, point.Y);
            for (int i = mHitLayers.Count - 1; i >= 0; i--)
            {
                var hit = mHitLayers[i];
                if (!Matrix4x4.Invert(hit.Transform, out var inverse))
                    continue;

                var local = Vector2.Transform(scenePoint, inverse);
                if (local.X >= 0 && local.X <= hit.Width &&
                    local.Y >= 0 && local.Y <= hit.Height)
                    return hit;
            }

            return null;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // The viewport is set at the start of OnPaint. Avoid making the
            // context current from WinForms' resize callback: GLControl may be
            // recreating its native child window at exactly this point.
            if (!mDisposing && IsHandleCreated)
                Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            mZoom = Math.Clamp(mZoom * (e.Delta > 0 ? 1.1f : 0.9f), 0.05f, 20.0f);
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            mPreviousMouse = e.Location;
            mPanning = e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right;
            if (e.Button == MouseButtons.Left && !mPanning)
            {
                mSelectedLayer = HitTest(e.Location);
                LayerSelected?.Invoke(this, mSelectedLayer?.Layer.Data.Name);
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (mPanning)
            {
                mPan += new Vector2(e.X - mPreviousMouse.X, e.Y - mPreviousMouse.Y);
                Invalidate();
            }
            mPreviousMouse = e.Location;
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            mPanning = false;
            base.OnMouseUp(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                bool canDisposeGl = TryMakeCurrent(allowDisposing: true);
                mDisposing = true;
                if (canDisposeGl)
                {
                    foreach (var atlas in mAtlases.Values)
                        atlas.Texture.Dispose();
                    mIndexBuffer?.Dispose();
                    mVertexBuffer?.Dispose();
                    mCanvasVertexBuffer?.Dispose();
                    if (mVertexArray != 0)
                        GL.DeleteVertexArray(mVertexArray);
                    if (mCanvasVertexArray != 0)
                        GL.DeleteVertexArray(mCanvasVertexArray);
                }

                mAtlases.Clear();
                mPendingResources = null;
            }

            base.Dispose(disposing);
        }

        private bool TryMakeCurrent(bool allowDisposing = false)
        {
            if (mDisposing || IsDisposed || (!allowDisposing && Disposing) ||
                !IsHandleCreated || Context == null)
                return false;

            try
            {
                MakeCurrent();
                return true;
            }
            catch (InvalidOperationException exception)
            {
                Debug.WriteLine($"AET GL context unavailable: {exception.Message}");
                return false;
            }
        }

        private static Color ToColor(uint value) => Color.FromArgb(
            (int)((value >> 24) & 0xFF), (int)((value >> 16) & 0xFF),
            (int)((value >> 8) & 0xFF), (int)(value & 0xFF));

        private static string CreateResourceKey(FgoSpriteResolution resolution) => string.Join("\u001f",
            resolution.Package.ArchivePath ?? resolution.Package.TablePath,
            resolution.Entry.TextureIndex, resolution.Entry.TextureName,
            resolution.Entry.AtlasWidth, resolution.Entry.AtlasHeight);

        private sealed record GpuAetAtlas(GLTexture Texture, int Width, int Height);

        private sealed record PreparedLayer(
            FgoAetRenderLayer Layer,
            Matrix4x4 Transform,
            float Opacity,
            bool Highlight,
            int Width,
            int Height);

        private sealed record HitLayer(
            FgoAetRenderLayer Layer,
            Matrix4x4 Transform,
            int Width,
            int Height);
    }
}
