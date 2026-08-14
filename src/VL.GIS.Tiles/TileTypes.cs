using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VL.GIS.Tiles;

/// <summary>
/// Which tile: column, row, and zoom level, in the XYZ / slippy-map convention.
/// </summary>
/// <remarks>
/// **This used to be BruTile's struct, and replacing it with our own is what lets VL.GIS and
/// VL.Mapsui be installed at the same time.** BruTile 6 and the BruTile 5 that Mapsui 4.1.9
/// requires carry different assembly versions and an incompatible <c>Attribution</c> layout, and
/// vvvv's package folder is flat — one version of each library for everything it loads. Every
/// other library the two packages share resolves to one identity (NetTopologySuite 2.5 and 2.6
/// are both assembly version 2.0.0.0), so BruTile was the whole conflict.
///
/// Being our own type has a second payoff that BruTile's could not give: this assembly carries
/// <c>[ImportAsIs]</c>, so Col, Row and Level are reachable in a patch. BruTile arrived as a plain
/// NugetDependency, which left its members visible only as raw .NET reflection nodes that the
/// NodeBrowser hides — the reason <c>Split</c> below had to exist at all.
/// </remarks>
public readonly record struct TileIndex(int Col, int Row, int Level);

/// <summary>
/// Where tiles come from: a URL to ask, the zoom levels it serves, and who must be credited.
/// </summary>
/// <remarks>
/// An interface rather than one concrete type so a test can stand in for the network — that is
/// what <c>FetchDeadlockTests</c> does, and the deadlock it guards against is not reproducible
/// against a real server.
///
/// There is deliberately only one of these. BruTile had <c>ITileSource</c> with
/// <c>IHttpTileSource</c> beneath it, and because every fetch node needed the derived one while
/// the factories were declared as the base, wiring a source into a fetch node was a downcast —
/// which VL does not insert. Nothing failed at compile time; the nodes appeared in the
/// NodeBrowser and simply refused to connect.
/// </remarks>
public interface ITileSource
{
    /// <summary>Shown to a patch author, not sent anywhere.</summary>
    string Name { get; }

    /// <summary>Lowest zoom level this source serves.</summary>
    int MinZoom { get; }

    /// <summary>Highest zoom level this source serves.</summary>
    int MaxZoom { get; }

    /// <summary>What the provider requires you to display. Empty if it asks for nothing.</summary>
    string AttributionText { get; }

    /// <summary>Where the attribution links to. Empty if there is none.</summary>
    string AttributionUrl { get; }

    /// <summary>
    /// The URL one tile would be fetched from, or empty if this source is not addressed by URL.
    /// </summary>
    /// <remarks>
    /// On the interface rather than only on <see cref="HttpTileSource"/> because a source can be
    /// wrapped — <c>WithAttribution</c> returns a decorator — and a <c>TileUrl</c> node that
    /// type-tested for the concrete class returned an empty string for every wrapped source,
    /// with nothing to say why.
    /// </remarks>
    string UrlFor(TileIndex index);

    /// <summary>The bytes of one tile, or null if it could not be fetched.</summary>
    Task<byte[]?> GetTileAsync(HttpClient client, TileIndex index, CancellationToken token = default);
}

/// <summary>
/// A tile source addressed by a URL template — the XYZ / slippy-map convention that OSM and
/// nearly every raster tile service speaks.
/// </summary>
/// <remarks>
/// The template takes <c>{z}</c> for zoom, <c>{x}</c> for column and <c>{y}</c> for row. That is
/// the whole of it: this replaced BruTile's HttpTileSource, which did the same substitution plus
/// a schema, predefined sources and WMTS. The schema was only ever read back for its zoom range,
/// the predefined sources were two URLs, and BruTile 6 had already removed the WMTS parser.
/// </remarks>
public sealed class HttpTileSource : ITileSource
{
    readonly string _urlTemplate;

    /// <param name="urlTemplate">e.g. <c>https://tile.openstreetmap.org/{z}/{x}/{y}.png</c></param>
    /// <param name="name">Shown to a patch author; not sent anywhere.</param>
    /// <param name="minZoom">Lowest zoom level this source serves.</param>
    /// <param name="maxZoom">Highest zoom level this source serves.</param>
    /// <param name="attributionText">What the provider requires you to display.</param>
    /// <param name="attributionUrl">Where that attribution links to.</param>
    public HttpTileSource(
        string urlTemplate,
        string name = "XYZ",
        int minZoom = 0,
        int maxZoom = 19,
        string attributionText = "",
        string attributionUrl = "")
    {
        _urlTemplate = urlTemplate ?? throw new ArgumentNullException(nameof(urlTemplate));
        Name = name;
        MinZoom = minZoom;
        MaxZoom = maxZoom;
        AttributionText = attributionText;
        AttributionUrl = attributionUrl;
    }

    /// <inheritdoc />
    public string Name { get; }
    /// <inheritdoc />
    public int MinZoom { get; }
    /// <inheritdoc />
    public int MaxZoom { get; }
    /// <inheritdoc />
    public string AttributionText { get; }
    /// <inheritdoc />
    public string AttributionUrl { get; }

    /// <inheritdoc />
    public string UrlFor(TileIndex index) => _urlTemplate
        .Replace("{z}", index.Level.ToString())
        .Replace("{x}", index.Col.ToString())
        .Replace("{y}", index.Row.ToString());

    /// <inheritdoc />
    public async Task<byte[]?> GetTileAsync(
        HttpClient client, TileIndex index, CancellationToken token = default)
    {
        using var response = await client.GetAsync(UrlFor(index), token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
    }
}

/// <summary>
/// Tiles kept on disk as <c>{level}/{col}/{row}.png</c>, so a restart does not refetch a view
/// that has already been looked at.
/// </summary>
/// <remarks>
/// Storing tiles that were drawn is what OpenStreetMap's tile usage policy asks for; what it
/// forbids is the opposite, fetching tiles nobody is looking at. Nothing here fetches anything.
/// </remarks>
public sealed class TileFileCache
{
    /// <param name="directory">Where tiles are written. Created on first save.</param>
    public TileFileCache(string directory)
        => Directory = directory ?? throw new ArgumentNullException(nameof(directory));

    /// <summary>Where the tiles are.</summary>
    public string Directory { get; }

    string PathFor(TileIndex index)
        => Path.Combine(Directory, index.Level.ToString(), index.Col.ToString(), index.Row + ".png");

    /// <summary>The cached bytes, or null if this tile has never been stored.</summary>
    public byte[]? Find(TileIndex index)
    {
        var path = PathFor(index);
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A cache that cannot be read is a slow cache, not an error worth throwing into a frame.
            return null;
        }
    }

    /// <summary>Store one tile. Returns the file it was written to, or null if it could not be.</summary>
    public string? Save(TileIndex index, byte[] bytes)
    {
        var path = PathFor(index);
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                  or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
