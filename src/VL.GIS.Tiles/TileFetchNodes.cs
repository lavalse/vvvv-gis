using BruTile;
using BruTile.Cache;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace VL.GIS.Tiles;

/// <summary>
/// Tile fetching, indexing, and coordinate conversion nodes for vvvv.
/// Category: GIS.Tiles
/// </summary>
public static class TileFetchNodes
{
    // ── Tile Index ────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a TileIndex from explicit col/row/level values.
    /// Tile coordinates follow the XYZ/slippy map convention.
    /// </summary>
    public static TileIndex CreateTileIndex(int col, int row, int level)
        => new TileIndex(col, row, level);

    /// <summary>
    /// Compute the TileIndex for a given longitude/latitude at a specific zoom level.
    /// Uses Web Mercator / OSM tile numbering convention.
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
    /// Fetch a single tile as raw bytes synchronously.
    /// Returns null on failure (e.g. 404, network error).
    /// </summary>
    public static byte[]? FetchTileBytes(ITileSource tileSource, TileIndex tileIndex)
    {
        try
        {
            return tileSource.GetTileAsync(new TileInfo { Index = tileIndex })
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
    /// </summary>
    public static IObservable<byte[]?> FetchTileAsync(
        ITileSource tileSource,
        TileIndex tileIndex)
        => Observable.FromAsync(async ct =>
        {
            try
            {
                return await tileSource.GetTileAsync(
                    new TileInfo { Index = tileIndex });
            }
            catch
            {
                return null;
            }
        });

    /// <summary>
    /// Fetch multiple tiles in parallel, returning an IObservable that emits
    /// (TileIndex, bytes) pairs as they arrive.
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
                        byte[]? bytes = await tileSource.GetTileAsync(
                            new TileInfo { Index = localIdx });
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
    /// </summary>
    public static FileCache CreateFileCache(string cacheDirectory)
        => new FileCache(cacheDirectory, "tile");

    /// <summary>
    /// Check whether a tile is present in a file cache without fetching it.
    /// </summary>
    public static bool IsTileCached(FileCache fileCache, TileIndex tileIndex)
        => fileCache.Find(tileIndex) != null;
}
