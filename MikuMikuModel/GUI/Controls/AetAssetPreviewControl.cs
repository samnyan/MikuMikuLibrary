using MikuMikuLibrary.Aets;
using MikuMikuLibrary.Aets.Resources;
using MikuMikuModel.Resources;

namespace MikuMikuModel.GUI.Controls;

/// <summary>Displays a resolved AET image asset.</summary>
public sealed class AetAssetPreviewControl : UserControl
{
    private static AetAssetPreviewControl sInstance;

    private readonly Label mStatusLabel = new();
    private readonly PictureBox mPictureBox = new();
    private Bitmap mBitmap;

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
        DisposeBitmap();

        if (resolution == null)
        {
            SetStatus("Resource not found");
            return;
        }

        try
        {
            if (!FgoSpriteBitmap.TryCrop(resolution, out mBitmap))
            {
                SetStatus("Invalid rectangle");
                return;
            }

            mPictureBox.Image = mBitmap;
            mStatusLabel.Text = "Resolved";
            mStatusLabel.ForeColor = Color.DarkGreen;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
        {
            SetStatus("Decode error");
        }
        catch (TypeInitializationException)
        {
            SetStatus("Decode error");
        }
        catch (Exception)
        {
            SetStatus("Decode error");
        }
    }

    private void SetStatus(string status)
    {
        mPictureBox.Image = null;
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
            DisposeBitmap();
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

        mStatusLabel.Dock = DockStyle.Bottom;
        mStatusLabel.AutoSize = false;
        mStatusLabel.Height = 24;
        mStatusLabel.Padding = new Padding(6, 3, 6, 3);
        mStatusLabel.Font = new Font(mStatusLabel.Font, FontStyle.Bold);

        Controls.Add(mPictureBox);
        Controls.Add(mStatusLabel);
        SetStatus("No asset selected");
    }
}
