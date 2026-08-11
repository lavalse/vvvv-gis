using BruTile;
using BruTile.Cache;
using BruTile.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using VL.Core.Import;

namespace VL.GIS.Tiles;

/// <summary>
/// Tile fetching and indexing nodes.
/// </summary>
[SkipCategory]
public static class TileFetchNodes
{
    // Shared HttpClient — reuse across all fetch calls (BruTile 6.0 requires caller to supply it)
    private static readonly HttpClient SharedClient;

    static TileFetchNodes()
    {
        SharedClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        SharedClient.DefaultRequestHeaders.Add("User-Agent", "VL.GIS/0.1 (vvvv gamma)");
    }

    // ── Tile Index ────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a TileIndex from explicit col/row/level values.
    /// Tile coordinates follow the XYZ/slippy map convention.
    /// Uses BruTile.
    /// </summary>
    public static TileIndex CreateTileIndex(int col, int row, int level)
        => new TileIndex(col, row, level);

    /// <summary>
    /// Split a TileIndex into its column, row and zoom level.
    /// Uses BruTile.
    /// </summary>
    public static void TileIndexParts(
        TileIndex tileIndex, out int col, out int row, out int level)
    {
        // TileIndex is a BruTile struct, so an IOBox can only display its type name, and
        // BruTile is a plain NugetDependency rather than an [ImportAsIs] assembly, which
        // leaves Col/Row/Level reachable only as raw .NET reflection nodes that the
        // NodeBrowser hides. Without this node the first question you ask when a tile looks
        // wrong -- which tile am I requesting -- cannot be answered inside a patch.
        col   = tileIndex.Col;
        row   = tileIndex.Row;
        level = tileIndex.Level;
    }

    /// <summary>
    /// Compute the TileIndex for a given longitude/latitude at a specific zoom level.
    /// Uses the Web Mercator / OSM tile numbering convention.
    /// Own arithmetic; the returned TileIndex is BruTile's type.
    /// </summary>
    public static TileIndex TileIndexFromLonLat(double longitude, double latitude, int zoom)
    {
        // OSM tile numbering: x from west, y from north
        int n = 1 << zoom;
        int x = (int)Math.Floor((longitude + 180.0) / 360.0 * n);
        int y = (int)Math.Floor((1.0 - Math.Log(
            Math.Tan(latitude * Math.PI / 180.0) + 1.0 / Math.Cos(latitude * Math.PI / 180.0)
        ) / Math.PI) / 2.0 * n);
        x = Math.Clamp(x, 0, n - 1);
        y = Math.Clamp(y, 0, n - 1);
        return new TileIndex(x, y, zoom);
    }

    /// <summary>
    /// Return all tile indices needed to cover a bounding box at a given zoom level.
    /// Useful for pre-fetching a region.
    /// Own arithmetic; TileIndex is BruTile's type.
    /// </summary>
    public static IReadOnlyList<TileIndex> TileIndicesForBounds(
        double minLon, double minLat,
        double maxLon, double maxLat,
        int zoom)
    {
        var min = TileIndexFromLonLat(minLon, maxLat, zoom); // NW corner
        var max = TileIndexFromLonLat(maxLon, minLat, zoom); // SE corner
        var result = new List<TileIndex>();
        for (int x = min.Col; x <= max.Col; x++)
            for (int y = min.Row; y <= max.Row; y++)
                result.Add(new TileIndex(x, y, zoom));
        return result;
    }

    /// <summary>
    /// Convert a TileIndex back to its bounding box in WGS84 lon/lat.
    /// Returns SW corner (minLon, minLat) and NE corner (maxLon, maxLat).
    /// Own arithmetic; TileIndex is BruTile's type.
    /// </summary>
    public static void TileBounds(
        TileIndex tileIndex,
        out double minLon, out double minLat,
        out double maxLon, out double maxLat)
    {
        int zoom = tileIndex.Level;
        int n = 1 << zoom;
        minLon = tileIndex.Col / (double)n * 360.0 - 180.0;
        maxLon = (tileIndex.Col + 1) / (double)n * 360.0 - 180.0;
        maxLat = Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * tileIndex.Row / n))) * 180.0 / Math.PI;
        minLat = Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * (tileIndex.Row + 1) / n))) * 180.0 / Math.PI;
    }

    // ── Synchronous Fetch ─────────────────────────────────────────────────────

    /// <summary>
    /// Fetch a single tile as raw bytes, blocking until it arrives.
    /// Returns null on failure (404, network error, timeout).
    /// Stalls the patch for the length of the request, so do not evaluate it every frame:
    /// wrap it in a Cache region driven by the tile index, or by the region's Force pin, and
    /// take FetchTileAsync instead whenever tiles change as the patch runs.
    /// See help/HowTo Fetch a map tile.vl.
    /// Uses BruTile.
    /// </summary>
    public static byte[]? FetchTileBytes(IHttpTileSource tileSource, TileIndex tileIndex)
    {
        try
        {
            // Task.Run is not decoration. Awaiting GetTileAsync directly on the calling
            // thread captures that thread's SynchronizationContext, and vvvv's runtime
            // thread has one: the continuation is posted back to the very thread that is
            // sitting in GetResult() waiting for it, and neither side ever moves. It
            // deadlocked vvvv hard enough that the runtime stopped evaluating, F5 could not
            // restart it, the log stayed empty, and closing the window left the process
            // alive. Task.Run moves the await onto a thread pool thread, where there is no
            // context to post back to.
            //
            // This is also why it appeared to work when called from PowerShell, which has
            // no SynchronizationContext at all. Testing sync-over-async in a host without
            // one proves nothing about a host that has one.
            return Task.Run(() =>
                       tileSource.GetTileAsync(SharedClient, new TileInfo { Index = tileIndex }))
                       .GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fetch a tile and write it to disk. Returns the file path on success.
    /// Useful for caching tiles locally.
    /// Uses BruTile.
    /// </summary>
    public static string? FetchTileToFile(
        IHttpTileSource tileSource,
        TileIndex tileIndex,
        string cacheDirectory)
    {
        var bytes = FetchTileBytes(tileSource, tileIndex);
        if (bytes == null) return null;

        Directory.CreateDirectory(cacheDirectory);
        string path = Path.Combine(
            cacheDirectory,
            $"{tileIndex.Level}_{tileIndex.Col}_{tileIndex.Row}.png");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ── Async / Observable Fetch ──────────────────────────────────────────────

    /// <summary>
    /// Fetch a tile asynchronously, returning an IObservable that emits the bytes once.
    /// In vvvv, connect to an S+H node to latch the result.
    /// Emits null if the tile could not be fetched.
    /// Uses BruTile.
    /// </summary>
    public static IObservable<byte[]?> FetchTileAsync(
        IHttpTileSource tileSource,
        TileIndex tileIndex)
        => Observable.FromAsync(async ct =>
        {
            try
            {
                return await tileSource.GetTileAsync(SharedClient,
                    new TileInfo { Index = tileIndex }, ct);
            }
            catch
            {
                return null;
            }
        });

    /// <summary>
    /// Fetch multiple tiles in parallel, returning an IObservable that emits
    /// (TileIndex, bytes) pairs as they arrive.
    /// Uses BruTile.
    /// </summary>
    public static IObservable<(TileIndex index, byte[]? bytes)> FetchTilesAsync(
        IHttpTileSource tileSource,
        IEnumerable<TileIndex> tileIndices)
    {
        return Observable.Create<(TileIndex, byte[]?)>(async (observer, ct) =>
        {
            var tasks = new List<Task>();
            foreach (var idx in tileIndices)
            {
                var localIdx = idx;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        byte[]? bytes = await tileSource.GetTileAsync(SharedClient,
                            new TileInfo { Index = localIdx }, ct);
                        observer.OnNext((localIdx, bytes));
                    }
                    catch
                    {
                        observer.OnNext((localIdx, null));
                    }
                }, ct));
            }
            await Task.WhenAll(tasks);
            observer.OnCompleted();
        });
    }

    // ── File-based Cache ─────────────────────────────────────────────────────

    /// <summary>
    /// Create a persistent file-system tile cache at the given directory.
    /// Tiles are stored as {level}/{col}/{row}.tile files.
    /// Uses BruTile.
    /// </summary>
    public static FileCache CreateFileCache(string cacheDirectory)
        => new FileCache(cacheDirectory, "tile");

    /// <summary>
    /// Check whether a tile is present in a file cache without fetching it.
    /// Uses BruTile.
    /// </summary>
    public static bool IsTileCached(FileCache fileCache, TileIndex tileIndex)
        => fileCache.Find(tileIndex) != null;
}
