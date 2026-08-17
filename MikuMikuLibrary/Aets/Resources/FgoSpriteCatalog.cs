namespace MikuMikuLibrary.Aets.Resources;

/// <summary>Result of resolving an AET asset to an FGO Sprite entry.</summary>
public sealed class FgoSpriteResolution
{
    /// <summary>Gets the package containing the Sprite.</summary>
    public FgoSpritePackage Package { get; }

    /// <summary>Gets the matching Sprite entry.</summary>
    public FgoSpriteEntry Entry { get; }

    /// <summary>Creates a resolution result.</summary>
    public FgoSpriteResolution(FgoSpritePackage package, FgoSpriteEntry entry)
    {
        Package = package ?? throw new ArgumentNullException(nameof(package));
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }
}

/// <summary>Scans FGO Sprite table files and resolves AET asset names.</summary>
public sealed class FgoSpriteCatalog : IDisposable
{
    private readonly Dictionary<string, List<FgoSpriteResolution>> mSprites =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FgoSpritePackage> mPackages;

    /// <summary>Gets the directory that was scanned.</summary>
    public string RootPath { get; }

    /// <summary>Gets all discovered Sprite packages.</summary>
    public IReadOnlyList<FgoSpritePackage> Packages => mPackages;

    /// <summary>Gets the number of loaded texture packages.</summary>
    public int LoadedTexturePackageCount => mPackages.Count(x => x.IsTextureLoaded);

    /// <summary>Gets the number of indexed Sprite names.</summary>
    public int SpriteCount => mSprites.Values.Sum(x => x.Count);

    /// <summary>Scans a directory for <c>spr_*_table.bin</c> and matching FARc files.</summary>
    /// <param name="rootPath">The game root or its <c>sprite</c> directory.</param>
    /// <returns>A catalog containing parsed tables and lazy archive paths.</returns>
    public static FgoSpriteCatalog Scan(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        string fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot))
            throw new DirectoryNotFoundException($"Sprite resource directory was not found: {fullRoot}");

        string spriteRoot = Directory.Exists(Path.Combine(fullRoot, "sprite"))
            ? Path.Combine(fullRoot, "sprite")
            : fullRoot;

        var tableFiles = Directory.EnumerateFiles(spriteRoot, "spr_*_table.bin", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var archives = Directory.EnumerateFiles(spriteRoot, "spr_*.farc", SearchOption.AllDirectories)
            .GroupBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var packages = new List<FgoSpritePackage>(tableFiles.Length);
        foreach (string tablePath in tableFiles)
        {
            string tableName = Path.GetFileName(tablePath);
            string packageName = tableName[..^"_table.bin".Length];
            string archiveName = packageName + ".farc";
            string archivePath = archives.TryGetValue(archiveName, out string foundArchive)
                ? foundArchive
                : Path.Combine(Path.GetDirectoryName(tablePath)!, archiveName);

            if (!File.Exists(archivePath))
                archivePath = null;

            try
            {
                packages.Add(new FgoSpritePackage(tablePath, archivePath));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // A partially installed or modded game may contain unrelated
                // files matching the name convention. Keep scanning the rest.
            }
        }

        return new FgoSpriteCatalog(fullRoot, packages);
    }

    /// <summary>Resolves a raw AET asset path or Sprite name.</summary>
    /// <param name="name">The asset path/name to resolve.</param>
    /// <returns>The first matching Sprite, or <see langword="null"/>.</returns>
    public FgoSpriteResolution Resolve(string name)
    {
        string key = FgoSpriteName.Normalize(name);
        return string.IsNullOrEmpty(key) || !mSprites.TryGetValue(key, out var matches)
            ? null
            : matches[0];
    }

    /// <summary>Disposes the catalog and releases all loaded package state.</summary>
    public void Dispose()
    {
        foreach (var package in mPackages)
            package.Dispose();
    }

    private FgoSpriteCatalog(string rootPath, List<FgoSpritePackage> packages)
    {
        RootPath = rootPath;
        mPackages = packages;

        foreach (var package in packages)
        foreach (var entry in package.Table.Entries)
        {
            string key = FgoSpriteName.Normalize(entry.Name);
            if (string.IsNullOrEmpty(key))
                continue;

            if (!mSprites.TryGetValue(key, out var matches))
                mSprites.Add(key, matches = new List<FgoSpriteResolution>());

            matches.Add(new FgoSpriteResolution(package, entry));
        }
    }
}
