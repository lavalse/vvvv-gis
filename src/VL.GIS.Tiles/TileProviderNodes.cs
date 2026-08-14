using VL.Core.Import;

namespace VL.GIS.Tiles;

/// <summary>
/// Map tile provider factory nodes. Creates tile sources for tile services.
/// </summary>
[SkipCategory]
public static class TileProviderNodes
{
    // ── OSM / OpenStreetMap ───────────────────────────────────────────────────

    // These used to return BruTile's IHttpTileSource, and getting that hierarchy wrong once cost
    // a day: the factories were declared as the base ITileSource while every fetch node took the
    // derived IHttpTileSource, so wiring one into the other was a downcast, which VL does not
    // insert. The nodes appeared in the NodeBrowser and simply refused to connect, with nothing
    // failing at compile time. Our own ITileSource has no derived interface, so that class of
    // failure is now impossible rather than merely avoided.

    /// <summary>
    /// Create an OpenStreetMap tile source (standard tile.openstreetmap.org).
    /// Returns tiles at zoom levels 0–19, 256×256 px, Web Mercator (EPSG:3857).
    /// VL.GIS's own implementation.
    /// </summary>
    public static ITileSource OsmTileSource()
        => new HttpTileSource(
            "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            name: "OpenStreetMap",
            minZoom: 0,
            maxZoom: 19,
            // Required, not decorative. The OSM tile usage policy obliges you to display this,
            // and TileAttribution is documented as the way to get it -- but nothing was passed
            // here originally, so it returned an empty string and anyone following the README
            // shipped no attribution at all.
            attributionText: "© OpenStreetMap contributors",
            attributionUrl: "https://www.openstreetmap.org/copyright");

    /// <summary>
    /// Create an OpenTopoMap tile source — topographic rendering of OSM data.
    /// VL.GIS's own implementation.
    /// </summary>
    public static ITileSource OpenTopoMapTileSource()
        => new HttpTileSource(
            "https://tile.opentopomap.org/{z}/{x}/{y}.png",
            name: "OpenTopoMap",
            minZoom: 0,
            maxZoom: 17,
            attributionText: "Map data: © OpenStreetMap contributors, SRTM | "
                + "Map style: © OpenTopoMap (CC-BY-SA)",
            attributionUrl: "https://opentopomap.org/");

    // ── XYZ / Slippy Map ──────────────────────────────────────────────────────

    /// <summary>
    /// Create a custom XYZ tile source from a URL template.
    /// Template variables: {z} = zoom, {x} = tile X, {y} = tile Y.
    /// Example: "https://example.com/tiles/{z}/{x}/{y}.png"
    /// VL.GIS's own implementation.
    /// </summary>
    /// <remarks>
    /// Attribution is a separate node rather than two more pins here. This had six inputs, and
    /// across the whole vvvv ecosystem 94% of nodes take three or fewer while not one designed
    /// node exceeds five — five means two decisions are wearing one node, and these are two:
    /// where the tiles come from, and who has to be credited for them. See docs/NODE-DESIGN.md.
    /// </remarks>
    public static ITileSource XyzTileSource(
        string urlTemplate,
        string name = "XYZ",
        int minZoom = 0,
        int maxZoom = 19)
        => new HttpTileSource(urlTemplate, name, minZoom, maxZoom);

    /// <summary>
    /// The same tile source, carrying the credit its provider requires you to display.
    /// Read it back with TileAttribution.
    /// VL.GIS's own implementation.
    /// </summary>
    public static ITileSource WithAttribution(
        ITileSource tileSource,
        string attributionText,
        string attributionUrl = "")
        => new AttributedTileSource(tileSource, attributionText, attributionUrl);

    // Wraps rather than rebuilds. Reconstructing an HttpTileSource here would mean recovering its
    // URL template, and the only way to see a template from outside is to ask for a tile's URL --
    // which substitutes the placeholders, so the "template" would come back as a single fixed
    // tile's address and every tile after the first would be the same one. Wrapping also keeps
    // this working for any ITileSource, including the test fake.
    sealed class AttributedTileSource : ITileSource
    {
        readonly ITileSource _inner;

        public AttributedTileSource(ITileSource inner, string text, string url)
        {
            _inner = inner;
            AttributionText = text;
            AttributionUrl = url;
        }

        public string Name => _inner.Name;
        public int MinZoom => _inner.MinZoom;
        public int MaxZoom => _inner.MaxZoom;
        public string AttributionText { get; }
        public string AttributionUrl { get; }

        public string UrlFor(TileIndex index) => _inner.UrlFor(index);

        public System.Threading.Tasks.Task<byte[]?> GetTileAsync(
            System.Net.Http.HttpClient client, TileIndex index,
            System.Threading.CancellationToken token = default)
            => _inner.GetTileAsync(client, index, token);
    }

    // No WMTS source. There was a WmtsTileSource here whose entire body threw
    // NotSupportedException -- BruTile 6.0 removed the WmtsParser it was written against --
    // while the README and the package description both advertised WMTS support. A node that is
    // guaranteed to throw is worse than an absent one: it is discoverable, it wires up, and it
    // fails only once someone runs the patch.
    //
    // Serving WMTS properly means reading GetCapabilities and turning the GetTile template into
    // a tile source, which is its own piece of work. Until then, XyzTileSource with a
    // hand-written WMTS URL template covers most of the ground.

    // ── Tile Source Info ──────────────────────────────────────────────────────

    /// <summary>Return the tile source's name. VL.GIS's own implementation.</summary>
    public static string TileSourceName(ITileSource tileSource) => tileSource.Name;

    /// <summary>Return the min and max zoom levels the source serves. VL.GIS's own implementation.</summary>
    public static void TileSourceZoomRange(
        ITileSource tileSource,
        out int minZoom,
        out int maxZoom)
    {
        minZoom = tileSource.MinZoom;
        maxZoom = tileSource.MaxZoom;
    }

    /// <summary>Return the attribution text for a tile source. VL.GIS's own implementation.</summary>
    public static string TileAttribution(ITileSource tileSource) => tileSource.AttributionText;

    /// <summary>Return the URL the attribution links to, if the provider gave one.</summary>
    public static string TileAttributionUrl(ITileSource tileSource) => tileSource.AttributionUrl;
}
