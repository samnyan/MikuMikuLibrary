using System.Drawing.Imaging;
using MikuMikuLibrary.Aets.Resources;
using MikuMikuLibrary.Textures.Processing;

namespace MikuMikuModel.Resources;

/// <summary>Decodes and crops a resolved FGO Sprite entry for preview or export.</summary>
public static class FgoSpriteBitmap
{
    /// <summary>Resolves an AET path/name pair and crops its Sprite bitmap.</summary>
    /// <param name="path">The source path stored in the AET, if available.</param>
    /// <param name="name">The source display name.</param>
    /// <param name="resolution">The resolved resource, or <see langword="null"/>.</param>
    /// <param name="bitmap">The cropped bitmap, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the resource was resolved and cropped.</returns>
    public static bool TryResolveAndCrop(string path, string name,
        out FgoSpriteResolution resolution, out Bitmap bitmap)
    {
        resolution = AetResourceContext.Instance.Resolve(path, name);
        bitmap = null;
        return resolution != null && TryCrop(resolution, out bitmap);
    }

    /// <summary>
    /// Decodes the referenced atlas, applies the FGO texture flip, and crops
    /// the entry's rectangle.
    /// </summary>
    /// <param name="resolution">The resolved Sprite entry.</param>
    /// <param name="bitmap">The cropped bitmap, or <see langword="null"/> when its rectangle is invalid.</param>
    /// <returns><see langword="true"/> when a cropped bitmap was created.</returns>
    public static bool TryCrop(FgoSpriteResolution resolution, out Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        using var atlas = DecodeAtlas(resolution);
        return TryCrop(atlas, resolution.Entry, out bitmap);
    }

    /// <summary>Decodes and flips the complete atlas for a resolved Sprite.</summary>
    /// <param name="resolution">The resolved Sprite entry.</param>
    /// <returns>An owned, already-flipped atlas bitmap.</returns>
    public static Bitmap DecodeAtlas(FgoSpriteResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        using var decoded = TextureDecoder.DecodeToBitmap(resolution.Package.GetTexture(resolution.Entry));
        decoded.RotateFlip(RotateFlipType.Rotate180FlipX);
        return new Bitmap(decoded);
    }

    /// <summary>Crops a Sprite rectangle from an already-flipped atlas.</summary>
    /// <param name="atlas">The already-flipped atlas bitmap.</param>
    /// <param name="entry">The Sprite table entry.</param>
    /// <param name="bitmap">The cropped bitmap, or <see langword="null"/> when its rectangle is invalid.</param>
    /// <returns><see langword="true"/> when a cropped bitmap was created.</returns>
    public static bool TryCrop(Bitmap atlas, FgoSpriteEntry entry, out Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(entry);

        bitmap = null;

        Rectangle rectangle = GetCropRectangle(entry, atlas.Width, atlas.Height);
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
            return false;

        bitmap = atlas.Clone(rectangle, PixelFormat.Format32bppArgb);
        return true;
    }

    private static Rectangle GetCropRectangle(FgoSpriteEntry entry, int width, int height)
    {
        double sourceLeft = Math.Min(entry.X0, entry.X1);
        double sourceTop = Math.Min(entry.Y0, entry.Y1);
        double sourceRight = Math.Max(entry.X0, entry.X1);
        double sourceBottom = Math.Max(entry.Y0, entry.Y1);

        // Most FGO tables use coordinates in the physical texture's pixel
        // space, even when small textures are padded to a larger GPU size.
        // Apply scaling only when the logical atlas exceeds the decoded size.
        double scaleX = entry.AtlasWidth > width && sourceRight > width
            ? (double)width / entry.AtlasWidth
            : 1.0;
        double scaleY = entry.AtlasHeight > height && sourceBottom > height
            ? (double)height / entry.AtlasHeight
            : 1.0;

        int left = (int)Math.Floor(sourceLeft * scaleX);
        int top = (int)Math.Floor(sourceTop * scaleY);
        int right = (int)Math.Ceiling(sourceRight * scaleX);
        int bottom = (int)Math.Ceiling(sourceBottom * scaleY);

        left = Math.Clamp(left, 0, width);
        top = Math.Clamp(top, 0, height);
        right = Math.Clamp(right, 0, width);
        bottom = Math.Clamp(bottom, 0, height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}
