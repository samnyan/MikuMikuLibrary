using System.Collections.Concurrent;
using System.Threading;
using MikuMikuLibrary.Aets.Resources;

namespace MikuMikuModel.Resources;

/// <summary>
/// Asynchronously loads and caches flipped FGO Sprite atlases.
/// </summary>
/// <remarks>
/// The cache owns decoded atlas bitmaps. Callers receive an independent
/// cropped bitmap, so the renderer may dispose it after drawing while the
/// atlas remains available for another frame. This separates archive I/O and
/// texture decoding from frame composition and is reusable by animation.
/// </remarks>
public sealed class FgoSpriteBitmapCache : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task<Bitmap>>> mAtlases = new();
    private int mDisposed;

    /// <summary>Gets the number of atlas load tasks currently cached.</summary>
    public int AtlasCount => mAtlases.Count;

    /// <summary>Loads and crops one Sprite on a thread-pool thread.</summary>
    /// <param name="resolution">The resolved Sprite entry.</param>
    /// <param name="cancellationToken">Cancels waiting or decoding before it starts.</param>
    /// <returns>A caller-owned cropped bitmap, or <see langword="null"/> for an invalid rectangle.</returns>
    public async Task<Bitmap> LoadSpriteAsync(FgoSpriteResolution resolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ThrowIfDisposed();

        var lazy = mAtlases.GetOrAdd(CreateKey(resolution), _ =>
            new Lazy<Task<Bitmap>>(
                () => DecodeAtlasAsync(resolution),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var atlas = await LoadAtlasAsync(resolution, cancellationToken).ConfigureAwait(false);
            if (atlas == null)
                return null;

            // GDI+ bitmap operations are not guaranteed to be concurrent. The
            // lock only covers the inexpensive rectangle clone; atlas decoding
            // and archive I/O happen outside it.
            lock (atlas)
            {
                return FgoSpriteBitmap.TryCrop(atlas, resolution.Entry, out var bitmap)
                    ? bitmap
                    : null;
            }
        }
        catch
        {
            // Do not retain failed archive/decoder tasks. A later selection may
            // succeed after a transient file or native decoder problem.
            if (lazy.IsValueCreated && lazy.Value.IsCompleted &&
                (lazy.Value.IsFaulted || lazy.Value.IsCanceled))
                mAtlases.TryRemove(new KeyValuePair<string, Lazy<Task<Bitmap>>>(
                    CreateKey(resolution), lazy));
            throw;
        }
    }

    /// <summary>Loads and retains the flipped atlas for GPU upload.</summary>
    /// <param name="resolution">The resolved Sprite entry.</param>
    /// <param name="cancellationToken">Cancels waiting for atlas decoding.</param>
    /// <returns>The cache-owned decoded atlas bitmap.</returns>
    public async Task<Bitmap> LoadAtlasAsync(FgoSpriteResolution resolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ThrowIfDisposed();

        var lazy = mAtlases.GetOrAdd(CreateKey(resolution), _ =>
            new Lazy<Task<Bitmap>>(
                () => DecodeAtlasAsync(resolution),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompleted &&
                (lazy.Value.IsFaulted || lazy.Value.IsCanceled))
                mAtlases.TryRemove(new KeyValuePair<string, Lazy<Task<Bitmap>>>(
                    CreateKey(resolution), lazy));
            throw;
        }
    }

    /// <summary>Releases completed atlas bitmaps and forgets their entries.</summary>
    public void Clear()
    {
        foreach (var pair in mAtlases.ToArray())
        {
            if (!pair.Value.IsValueCreated || !pair.Value.Value.IsCompletedSuccessfully)
                continue;

            pair.Value.Value.Result?.Dispose();
            mAtlases.TryRemove(new KeyValuePair<string, Lazy<Task<Bitmap>>>(pair.Key, pair.Value));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref mDisposed, 1) != 0)
            return;

        foreach (var pair in mAtlases.Values)
        {
            if (pair.IsValueCreated && pair.Value.IsCompletedSuccessfully)
                pair.Value.Result?.Dispose();
        }

        mAtlases.Clear();
    }

    private async Task<Bitmap> DecodeAtlasAsync(FgoSpriteResolution resolution)
    {
        // DecodeAtlas is synchronous because the native decoder API is
        // synchronous; Task.Run keeps it, archive decompression, and file I/O
        // away from the WinForms thread.
        var atlas = await Task.Run(() => FgoSpriteBitmap.DecodeAtlas(resolution)).ConfigureAwait(false);
        if (Volatile.Read(ref mDisposed) == 0)
            return atlas;

        atlas.Dispose();
        throw new ObjectDisposedException(nameof(FgoSpriteBitmapCache));
    }

    private static string CreateKey(FgoSpriteResolution resolution)
    {
        var package = resolution.Package;
        var entry = resolution.Entry;
        return string.Join("\u001f",
            package.ArchivePath ?? package.TablePath,
            entry.TextureIndex,
            entry.TextureName,
            entry.AtlasWidth,
            entry.AtlasHeight);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref mDisposed) != 0)
            throw new ObjectDisposedException(nameof(FgoSpriteBitmapCache));
    }
}
