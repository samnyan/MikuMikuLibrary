using System.Threading;
using MikuMikuLibrary.Aets;
using MikuMikuLibrary.Aets.Resources;
using MikuMikuModel.Resources;

namespace MikuMikuModel.GUI.Controls;

/// <summary>Displays a resolved AET image asset.</summary>
public sealed class AetAssetPreviewControl : UserControl
{
    private static AetAssetPreviewControl sInstance;

    private readonly Label mStatusLabel = new();
    private readonly ProgressBar mProgressBar = new();
    private readonly PictureBox mPictureBox = new();
    private readonly FgoSpriteBitmapCache mBitmapCache = new();
    private Bitmap mBitmap;
    private CancellationTokenSource mLoadCancellation;
    private bool mDisposed;

    /// <summary>Gets the shared preview control instance.</summary>
    public static AetAssetPreviewControl Instance => sInstance ??= new();

    /// <summary>Shows an AET source and resolves its image through the current context.</summary>
    /// <param name="source">The parsed FGO AET source.</param>
    public void SetSource(FgoAetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var resolution = AetResourceContext.Instance.Resolve(source.Path, source.Name);
        SetResolvedSource(resolution);
    }

    /// <summary>Shows an AET source with an explicitly resolved Sprite.</summary>
    /// <param name="source">The parsed FGO AET source.</param>
    /// <param name="resolution">The resolved Sprite, or <see langword="null"/>.</param>
    public void SetSource(FgoAetSource source, FgoSpriteResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(source);
        SetResolvedSource(resolution);
    }

    /// <summary>Shows a Sprite table entry through the current resource context.</summary>
    /// <param name="name">The Sprite name used for diagnostics.</param>
    /// <param name="resolution">The resolved Sprite, or <see langword="null"/>.</param>
    public void SetSprite(string name, FgoSpriteResolution resolution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        // FGO stores Rectangle coordinates in the already-flipped atlas space.
        SetResolvedSource(resolution);
    }

    private void SetResolvedSource(FgoSpriteResolution resolution)
    {
        mLoadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        mLoadCancellation = cancellation;
        DisposeBitmap();

        if (resolution == null)
        {
            cancellation.Dispose();
            mLoadCancellation = null;
            SetStatus("Resource not found");
            return;
        }

        mProgressBar.Visible = true;
        mProgressBar.Style = ProgressBarStyle.Marquee;
        mStatusLabel.Text = "Loading sprite...";
        mStatusLabel.ForeColor = Color.DarkOrange;
        _ = LoadAndPresentAsync(resolution, cancellation);
    }

    private async Task LoadAndPresentAsync(FgoSpriteResolution resolution,
        CancellationTokenSource cancellation)
    {
        try
        {
            var bitmap = await mBitmapCache.LoadSpriteAsync(resolution, cancellation.Token);
            if (cancellation.IsCancellationRequested || mDisposed ||
                !ReferenceEquals(mLoadCancellation, cancellation))
            {
                bitmap?.Dispose();
                return;
            }

            if (bitmap == null)
            {
                SetStatus("Invalid rectangle");
                return;
            }

            mBitmap = bitmap;
            mPictureBox.Image = mBitmap;
            mProgressBar.Visible = false;
            mStatusLabel.Text = "Resolved";
            mStatusLabel.ForeColor = Color.DarkGreen;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
        {
            if (!cancellation.IsCancellationRequested && ReferenceEquals(mLoadCancellation, cancellation))
                SetStatus("Decode error");
        }
        catch (TypeInitializationException)
        {
            if (!cancellation.IsCancellationRequested && ReferenceEquals(mLoadCancellation, cancellation))
                SetStatus("Decode error");
        }
        catch (Exception)
        {
            if (!cancellation.IsCancellationRequested && ReferenceEquals(mLoadCancellation, cancellation))
                SetStatus("Decode error");
        }
        finally
        {
            cancellation.Dispose();
            if (ReferenceEquals(mLoadCancellation, cancellation))
                mLoadCancellation = null;
        }
    }

    private void SetStatus(string status)
    {
        mPictureBox.Image = null;
        mProgressBar.Visible = false;
        mStatusLabel.Text = status;
        mStatusLabel.ForeColor = status == "Resolved" ? Color.DarkGreen : Color.DarkRed;
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
            mLoadCancellation?.Cancel();
            DisposeBitmap();
            mBitmapCache.Dispose();
            mProgressBar.Dispose();
            mPictureBox.Dispose();
            mStatusLabel.Dispose();
        }

        base.Dispose(disposing);
    }

    private AetAssetPreviewControl()
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
        SetStatus("No asset selected");
    }
}
