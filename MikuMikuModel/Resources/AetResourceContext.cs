using MikuMikuLibrary.Aets;
using MikuMikuLibrary.Aets.Resources;

namespace MikuMikuModel.Resources;

/// <summary>Holds the user-selected game resource directory for AET previews.</summary>
/// <remarks>
/// The context is session state only. It never assumes a particular game
/// installation path and does not copy any game files into the application.
/// </remarks>
public sealed class AetResourceContext : IDisposable
{
    /// <summary>Gets the process-wide preview resource context used by nodes.</summary>
    public static AetResourceContext Instance { get; } = new();

    /// <summary>Gets the selected game root, or <see langword="null"/>.</summary>
    public string RootPath { get; private set; }

    /// <summary>Gets the currently indexed FGO Sprite catalog, or <see langword="null"/>.</summary>
    public FgoSpriteCatalog FgoSprites { get; private set; }

    /// <summary>Gets whether a resource directory is configured.</summary>
    public bool IsConfigured => FgoSprites != null;

    /// <summary>Raised after the selected resource directory or its indexes change.</summary>
    public event EventHandler Changed;

    /// <summary>Sets and scans a game resource directory.</summary>
    /// <param name="rootPath">The game root or its <c>sprite</c> subdirectory.</param>
    public void SetRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var catalog = FgoSpriteCatalog.Scan(rootPath);
        var previousCatalog = FgoSprites;

        RootPath = catalog.RootPath;
        FgoSprites = catalog;
        previousCatalog?.Dispose();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets one explicitly paired Sprite table and texture archive.</summary>
    /// <param name="tablePath">The physical <c>spr_*_table.bin</c> path.</param>
    /// <param name="archivePath">The matching or user-selected FARc path.</param>
    public void SetSpritePackage(string tablePath, string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        var catalog = FgoSpriteCatalog.Create(new FgoSpritePackage(tablePath, archivePath));
        var previousCatalog = FgoSprites;

        RootPath = catalog.RootPath;
        FgoSprites = catalog;
        previousCatalog?.Dispose();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears the selected directory and releases all cached resources.</summary>
    public void Clear()
    {
        var previousCatalog = FgoSprites;
        RootPath = null;
        FgoSprites = null;
        previousCatalog?.Dispose();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Resolves an AET path, falling back to its display name.</summary>
    /// <param name="path">The source path stored by AET.</param>
    /// <param name="name">The source display name.</param>
    /// <returns>A matching FGO Sprite, or <see langword="null"/>.</returns>
    public FgoSpriteResolution Resolve(string path, string name = null)
    {
        if (FgoSprites == null)
            return null;

        return FgoSprites.Resolve(path) ?? FgoSprites.Resolve(name);
    }

    /// <summary>Resolves the source reference used by an FGO AET asset record.</summary>
    /// <param name="source">The parsed AET source entry.</param>
    /// <returns>The matching Sprite table entry, or <see langword="null"/>.</returns>
    public FgoSpriteResolution Resolve(FgoAetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return FgoSprites?.Resolve(source);
    }

    /// <inheritdoc />
    public void Dispose() => Clear();
}
