using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using MikuMikuLibrary.Aets.Resources;
using MikuMikuLibrary.Textures.Processing;

namespace MikuMikuModel.Resources;

/// <summary>Exports an FGO Sprite table as a Sprite Localization Studio v1 project.</summary>
public static class FgoSpriteTableLocalizationExporter
{
    private static readonly JsonSerializerOptions sJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Exports all atlases and the table metadata to an SLS project directory.</summary>
    /// <param name="tablePath">The physical <c>spr_*_table.bin</c> path.</param>
    /// <param name="table">The already parsed Sprite table.</param>
    /// <param name="destinationDirectory">The destination SLS project directory.</param>
    /// <param name="archivePath">The explicitly paired FARc path, or <see langword="null"/> to use the adjacent archive.</param>
    public static void Export(string tablePath, FgoSpriteTable table, string destinationDirectory, string archivePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePath);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        string fullTablePath = Path.GetFullPath(tablePath);
        string fullDestinationDirectory = Path.GetFullPath(destinationDirectory);
        archivePath = string.IsNullOrWhiteSpace(archivePath)
            ? GetAdjacentArchivePath(fullTablePath)
            : Path.GetFullPath(archivePath);

        if (archivePath == null || !File.Exists(archivePath))
            throw new FileNotFoundException(
                $"Matching texture archive '{Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fullTablePath))}.farc' was not found.");

        using var package = new FgoSpritePackage(fullTablePath, archivePath);
        var textureSet = package.LoadTextureSet();

        if (textureSet.Textures.Count != table.TextureNames.Count)
            throw new InvalidDataException(
                $"Sprite table has {table.TextureNames.Count} texture names, but '{Path.GetFileName(archivePath)}' contains " +
                $"{textureSet.Textures.Count} textures.");

        Directory.CreateDirectory(fullDestinationDirectory);

        var manifest = new SpriteTableManifest
        {
            Id = package.Name,
            Name = package.Name
        };

        var textureIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < textureSet.Textures.Count; index++)
        {
            string relativeTexturePath = GetTexturePath(table.TextureNames[index]);
            string textureId = GetTextureId(relativeTexturePath);
            string outputPath = Path.Combine(fullDestinationDirectory,
                "textures", relativeTexturePath.Replace('/', Path.DirectorySeparatorChar));

            if (!textureIds.Add(textureId))
                throw new InvalidDataException($"Sprite table contains duplicate texture ID '{textureId}'.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using (var bitmap = TextureDecoder.DecodeToBitmap(textureSet.Textures[index]))
            {
                // FGO's texture data is vertically inverted relative to the
                // Sprite table's top-left atlas coordinates.
                bitmap.RotateFlip(RotateFlipType.Rotate180FlipX);
                bitmap.Save(outputPath, ImageFormat.Png);

                manifest.Textures.Add(new TextureManifest
                {
                    Id = textureId,
                    ImagePath = relativeTexturePath,
                    Size = new SizeManifest { Width = bitmap.Width, Height = bitmap.Height }
                });
            }
        }

        var spriteIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in table.Entries)
        {
            if (entry.TextureIndex < 0 || entry.TextureIndex >= manifest.Textures.Count)
                throw new InvalidDataException(
                    $"Sprite '{entry.Name}' references texture index {entry.TextureIndex}, which is outside the table texture list.");

            var size = manifest.Textures[entry.TextureIndex].Size;
            Rectangle rectangle = FgoSpriteBitmap.GetCropRectangle(entry, size.Width, size.Height);
            if (rectangle.Width <= 0 || rectangle.Height <= 0)
                throw new InvalidDataException($"Sprite '{entry.Name}' has an empty atlas rectangle.");

            if (string.IsNullOrWhiteSpace(entry.Name) || !spriteIds.Add(entry.Name))
                throw new InvalidDataException($"Sprite table contains an empty or duplicate sprite ID '{entry.Name}'.");

            manifest.Sprites.Add(new SpriteManifest
            {
                Id = entry.Name,
                Name = entry.Name,
                TextureId = manifest.Textures[entry.TextureIndex].Id,
                Frame = new RectangleManifest
                {
                    X = rectangle.X,
                    Y = rectangle.Y,
                    Width = rectangle.Width,
                    Height = rectangle.Height
                },
                Rotation = 0,
                Trimmed = false
            });
        }

        string manifestPath = $"manifests/{package.Name}.sprite-table.json";
        WriteJson(Path.Combine(fullDestinationDirectory, "project.json"), new ProjectManifest
        {
            Name = package.Name,
            SpriteTableManifestPaths = new List<string> { manifestPath }
        });
        WriteJson(Path.Combine(fullDestinationDirectory,
            manifestPath.Replace('/', Path.DirectorySeparatorChar)), manifest);
    }

    private static string GetAdjacentArchivePath(string tablePath)
    {
        const string suffix = "_table.bin";
        string tableName = Path.GetFileName(tablePath);
        if (!tableName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return null;

        string archivePath = Path.Combine(Path.GetDirectoryName(tablePath)!,
            tableName[..^suffix.Length] + ".farc");
        return File.Exists(archivePath) ? archivePath : null;
    }

    private static string GetTexturePath(string textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName))
            throw new InvalidDataException("Sprite table contains an empty texture name.");

        string normalizedName = textureName.Replace('\\', '/').Trim('/');
        string[] segments = normalizedName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." ||
            segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidDataException($"Sprite table texture name '{textureName}' cannot be exported as a file path.");
        }

        string fileName = segments[^1];
        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            segments[^1] = fileName + ".png";

        // The original table texture name becomes the SLS image name; this is
        // essential because the FARc stores only an ordered texture array.
        return string.Join('/', segments);
    }

    private static string GetTextureId(string imagePath)
    {
        string fileName = imagePath[(imagePath.LastIndexOf('/') + 1)..];
        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Sprite texture path '{imagePath}' does not end with .png.");

        return fileName[..^".png".Length];
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(value, sJsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private sealed class ProjectManifest
    {
        public int SchemaVersion { get; } = 1;
        public string Name { get; init; }
        public List<string> SpriteTableManifestPaths { get; init; }
    }

    private sealed class SpriteTableManifest
    {
        public int SchemaVersion { get; } = 1;
        public string Id { get; init; }
        public string Name { get; init; }
        public List<TextureManifest> Textures { get; } = new();
        public List<SpriteManifest> Sprites { get; } = new();
    }

    private sealed class TextureManifest
    {
        public string Id { get; init; }
        public string ImagePath { get; init; }
        public SizeManifest Size { get; init; }
    }

    private sealed class SpriteManifest
    {
        public string Id { get; init; }
        public string Name { get; init; }
        public string TextureId { get; init; }
        public RectangleManifest Frame { get; init; }
        public int Rotation { get; init; }
        public bool Trimmed { get; init; }
    }

    private sealed class SizeManifest
    {
        public int Width { get; init; }
        public int Height { get; init; }
    }

    private sealed class RectangleManifest
    {
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }
}
