using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading.Tasks;
using VL.Core.Import;

namespace VL.GIS.Tiles;

/// <summary>
/// Tile fetching and indexing nodes.
/// </summary>
[SkipCategory]
public static class TileFetchNodes
{
    // One HttpClient for every fetch. A client per call exhausts sockets, which is the same
    // failure the runtime rules are about, one layer down.
    private static readonly HttpClient SharedClient;

    static TileFetchNodes()
    {
        SharedClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // OSM's tile usage policy requires a User-Agent identifying the application.
        SharedClient.DefaultRequestHeaders.Add(
            "User-Agent", "VL.GIS/0.2 (+https://github.com/rednotfound/vvvv-gis)");
    }

    // ── Tile Index ────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a TileIndex from explicit col/row/level values.
    /// Tile coordinates follow the XYZ/slippy map convention.
    /// VL.GIS's own implementation.
    /// </summary>
    public static TileIndex CreateTileIndex(int col, int row, int level)
        => new TileIndex(col, row, level);

    /// <summary>
    /// Split a TileIndex into its column, row and zoom level.
    /// VL.GIS's own implementation.
    /// </summary>
    /// <remarks>
    /// Named Split because that is the ecosystem's word for opening a value — it appears 194
    /// times in the help patches shipped with vvvv, so it is what someone types into the
    /// NodeBrowser. It was TileIndexParts, which is a word only we used.
    ///
    /// Without it the first question you ask when a tile looks wrong — which tile am I
    /// requesting — could not be answered inside a patch, because BruTile's TileIndex arrived
    /// through a plain NugetDependency and its members were reachable only as raw .NET
    /// reflection nodes that the NodeBrowser hides. TileIndex is now our own type in an
    /// [ImportAsIs] assembly, so Col/Row/Level are reachable directly; this node stays because
    /// it is the discoverable name and it splits all three at once.
    /// </remarks>
    public static void Split(
        TileIndex tileIndex, out int col, out int row, out int level)
    {
        col   = tileIndex.Col;
        row   = tileIndex.Row;
        level = tileIndex.Level;
    }

    /// <summary>
    /// Compute the TileIndex for a given longitude/latitude at a specific zoom level.
    /// Uses the Web Mercator / OSM tile numbering convention.
    /// VL.GIS's own implementation.
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
    /// VL.GIS's own implementation.
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
    /// VL.GIS's own implementation.
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

    /// <summary>The URL a tile would be fetched from. Useful for checking a template by eye.</summary>
    public static string TileUrl(ITileSource tileSource, TileIndex tileIndex)
        => tileSource.UrlFor(tileIndex);

    // ── Synchronous Fetch ─────────────────────────────────────────────────────

    /// <summary>
    /// Fetch a single tile as raw bytes, blocking until it arrives.
    /// Returns null on failure (404, network error, timeout).
    /// Stalls the patch for the length of the request, so do not evaluate it every frame:
    /// wrap it in a Cache region driven by the tile index, or by the region's Force pin, and
    /// take FetchTileAsync instead whenever tiles change as the patch runs.
    /// See help/HowTo Fetch a map tile.vl.
    /// VL.GIS's own implementation.
    /// </summary>
    public static byte[]? FetchTileBytes(ITileSource tileSource, TileIndex tileIndex)
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
            //
            // Still true now that the fetch is our own code rather than BruTile's: the hazard
            // was never in the library, it was in awaiting anything on that thread.
            return Task.Run(() => tileSource.GetTileAsync(SharedClient, tileIndex))
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
    /// VL.GIS's own implementation.
    /// </summary>
    public static string? FetchTileToFile(
        ITileSource tileSource,
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
    /// VL.GIS's own implementation.
    /// </summary>
    public static IObservable<byte[]?> FetchTileAsync(
        ITileSource tileSource,
        TileIndex tileIndex)
        => Observable.FromAsync(async ct =>
        {
            try
            {
                return await tileSource.GetTileAsync(SharedClient, tileIndex, ct);
            }
            catch
            {
                return null;
            }
        });

    /// <summary>
    /// Fetch multiple tiles in parallel, returning an IObservable that emits
    /// (TileIndex, bytes) pairs as they arrive.
    /// VL.GIS's own implementation.
    /// </summary>
    public static IObservable<(TileIndex index, byte[]? bytes)> FetchTilesAsync(
        ITileSource tileSource,
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
                        byte[]? bytes = await tileSource.GetTileAsync(SharedClient, localIdx, ct);
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
    /// Tiles are stored as {level}/{col}/{row}.png files.
    /// VL.GIS's own implementation.
    /// </summary>
    public static TileFileCache CreateFileCache(string cacheDirectory)
        => new TileFileCache(cacheDirectory);

    /// <summary>
    /// Check whether a tile is present in a file cache without fetching it.
    /// VL.GIS's own implementation.
    /// </summary>
    public static bool IsTileCached(TileFileCache fileCache, TileIndex tileIndex)
        => fileCache.Find(tileIndex) != null;

    /// <summary>
    /// The cached bytes for a tile, or null if it has never been stored. Never fetches.
    /// VL.GIS's own implementation.
    /// </summary>
    public static byte[]? ReadCachedTile(TileFileCache fileCache, TileIndex tileIndex)
        => fileCache.Find(tileIndex);
}
