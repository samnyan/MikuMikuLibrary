using MikuMikuLibrary.Archives;
using MikuMikuLibrary.IO;
using MikuMikuLibrary.Textures;

namespace MikuMikuLibrary.Aets.Resources;

/// <summary>A lazily loaded FGO Sprite table and its optional texture archive.</summary>
public sealed class FgoSpritePackage : IDisposable
{
    private readonly object mTextureSetLock = new();
    private TextureSet mTextureSet;

    /// <summary>Gets the package name, without its file extension.</summary>
    public string Name { get; }

    /// <summary>Gets the Sprite table path.</summary>
    public string TablePath { get; }

    /// <summary>Gets the matching FARc path, when present.</summary>
    public string ArchivePath { get; }

    /// <summary>Gets the parsed Sprite table.</summary>
    public FgoSpriteTable Table { get; }

    /// <summary>Gets whether the texture archive has already been loaded.</summary>
    public bool IsTextureLoaded => mTextureSet != null;

    /// <summary>Creates a package from a table and an optional FARc path.</summary>
    /// <param name="tablePath">The table file path.</param>
    /// <param name="archivePath">The matching FARc path, or <see langword="null"/>.</param>
    public FgoSpritePackage(string tablePath, string archivePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePath);
        if (!File.Exists(tablePath))
            throw new FileNotFoundException("FGO Sprite table was not found.", tablePath);

        TablePath = Path.GetFullPath(tablePath);
        ArchivePath = string.IsNullOrWhiteSpace(archivePath) ? null : Path.GetFullPath(archivePath);
        Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(TablePath));
        Table = FgoSpriteTable.ParseFile(TablePath);
    }

    /// <summary>Gets a Sprite entry by normalized name.</summary>
    /// <param name="name">A Sprite name or an AET <c>.pic</c> path.</param>
    /// <returns>The matching entry, or <see langword="null"/>.</returns>
    public FgoSpriteEntry FindSprite(string name)
    {
        string normalized = FgoSpriteName.Normalize(name);
        return string.IsNullOrEmpty(normalized)
            ? null
            : Table.Entries.FirstOrDefault(x => FgoSpriteName.Normalize(x.Name) == normalized);
    }

    /// <summary>Loads the package texture set on first use.</summary>
    /// <returns>The decoded TXP3 texture set.</returns>
    public TextureSet LoadTextureSet()
    {
        lock (mTextureSetLock)
        {
            if (mTextureSet != null)
                return mTextureSet;

            if (string.IsNullOrEmpty(ArchivePath) || !File.Exists(ArchivePath))
                throw new FileNotFoundException("FGO Sprite texture archive was not found.", ArchivePath);

            using var archive = BinaryFile.Load<FgoFarcArchive>(ArchivePath);
            if (!archive.Contains("texture.bin"))
                throw new InvalidDataException($"FGO Sprite archive '{ArchivePath}' has no texture.bin entry.");

            using var stream = archive.Open("texture.bin", EntryStreamMode.MemoryStream);
            mTextureSet = BinaryFile.Load<TextureSet>(stream, leaveOpen: false);
            return mTextureSet;
        }
    }

    /// <summary>Gets the texture referenced by a Sprite entry.</summary>
    /// <param name="entry">The Sprite entry.</param>
    /// <returns>The atlas texture.</returns>
    public Texture GetTexture(FgoSpriteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var textureSet = LoadTextureSet();
        if (entry.TextureIndex < 0 || entry.TextureIndex >= textureSet.Textures.Count)
            throw new InvalidDataException(
                $"Sprite '{entry.Name}' references texture index {entry.TextureIndex}, " +
                $"but the package contains {textureSet.Textures.Count} textures.");

        return textureSet.Textures[entry.TextureIndex];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (mTextureSetLock)
        {
            mTextureSet?.Dispose();
            mTextureSet = null;
        }
    }
}

/// <summary>Provides a consistent name normalization routine for AET Sprite lookup.</summary>
public static class FgoSpriteName
{
    /// <summary>Normalizes a Sprite name or AET asset path.</summary>
    /// <param name="name">The raw name or path.</param>
    /// <returns>A case-insensitive lookup key.</returns>
    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string value = name.Trim().Replace('\\', '/');
        int lastSlash = value.LastIndexOf('/');
        if (lastSlash >= 0)
            value = value[(lastSlash + 1)..];

        if (value.EndsWith(".pic", StringComparison.OrdinalIgnoreCase))
            value = value[..^4];

        return value.Trim().ToUpperInvariant();
    }
}
