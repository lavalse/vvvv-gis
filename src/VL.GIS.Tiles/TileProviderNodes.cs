using BruTile;
using BruTile.Cache;
using BruTile.Predefined;
using BruTile.Web;
using System;
using System.Collections.Generic;
using System.Net.Http;
using VL.Core.Import;

namespace VL.GIS.Tiles;

/// <summary>
/// Map tile provider factory nodes. Creates ITileSource instances for tile services.
/// </summary>
[SkipCategory]
public static class TileProviderNodes
{
    // Shared HttpClient for all web tile sources
    private static readonly HttpClient SharedHttpClient;

    static TileProviderNodes()
    {
        SharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        SharedHttpClient.DefaultRequestHeaders.Add("User-Agent", "VL.GIS/0.1 (vvvv gamma)");
    }

    // ── OSM / OpenStreetMap ───────────────────────────────────────────────────

    // These return IHttpTileSource, not ITileSource, and the difference decides whether
    // the category works at all. BruTile's hierarchy is
    //
    //     ITileSource
    //       +-- IHttpTileSource     <- HttpTileSource, what these factories construct
    //       +-- ILocalTileSource
    //
    // and every fetch node takes an IHttpTileSource, because fetching needs the HttpClient
    // overload of GetTileAsync. Declaring ITileSource here made the output pin the base
    // interface, so wiring a source into FetchTileBytes would have been a downcast, which
    // VL does not insert for you. Nothing about that shows up at compile time -- the nodes
    // appear in the NodeBrowser and simply refuse to connect.
    //
    // The schema and attribution nodes below still take ITileSource; upcasting to it stays
    // implicit, so they keep accepting these.

    /// <summary>
    /// Create an OpenStreetMap tile source (standard tile.openstreetmap.org).
    /// Returns tiles at zoom levels 0–19, 256×256 px, Web Mercator (EPSG:3857).
    /// </summary>
    public static IHttpTileSource OsmTileSource()
        => new HttpTileSource(
            new GlobalSphericalMercator(0, 19),
            "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            name: "OpenStreetMap",
            configureHttpRequestMessage: msg =>
                msg.Headers.Add("User-Agent", "VL.GIS/0.1 (vvvv gamma)"));

    /// <summary>
    /// Create an OpenTopoMap tile source — topographic rendering of OSM data.
    /// </summary>
    public static IHttpTileSource OpenTopoMapTileSource()
        => new HttpTileSource(
            new GlobalSphericalMercator(0, 17),
            "https://tile.opentopomap.org/{z}/{x}/{y}.png",
            name: "OpenTopoMap",
            configureHttpRequestMessage: msg =>
                msg.Headers.Add("User-Agent", "VL.GIS/0.1 (vvvv gamma)"));

    // ── XYZ / Slippy Map ──────────────────────────────────────────────────────

    /// <summary>
    /// Create a custom XYZ tile source from a URL template.
    /// Template variables: {z} = zoom, {x} = tile X, {y} = tile Y.
    /// Example: "https://example.com/tiles/{z}/{x}/{y}.png"
    /// </summary>
    public static IHttpTileSource XyzTileSource(
        string urlTemplate,
        string name = "XYZ",
        int minZoom = 0,
        int maxZoom = 19)
        => new HttpTileSource(
            new GlobalSphericalMercator(minZoom, maxZoom),
            urlTemplate,
            name: name,
            configureHttpRequestMessage: msg =>
                msg.Headers.Add("User-Agent", "VL.GIS/0.1 (vvvv gamma)"));

    // No WMTS source. There was a WmtsTileSource here whose entire body threw
    // NotSupportedException -- BruTile 6.0 removed the WmtsParser it was written against --
    // while the README and the package description both advertised WMTS support. A node
    // that is guaranteed to throw is worse than an absent one: it is discoverable, it
    // wires up, and it fails only once someone runs the patch.
    //
    // Serving WMTS properly means reading GetCapabilities and turning the GetTile template
    // into a tile source, which is its own piece of work. Until then, XyzTileSource with a
    // hand-written WMTS URL template covers most of the ground.

    // ── Tile Schema Info ──────────────────────────────────────────────────────

    /// <summary>Return the tile schema name (e.g. "GlobalSphericalMercator").</summary>
    public static string TileSchemaName(ITileSource tileSource) => tileSource.Schema.Name;

    /// <summary>Return the min and max zoom levels of the tile schema.</summary>
    public static void TileSchemaZoomRange(
        ITileSource tileSource,
        out int minZoom,
        out int maxZoom)
    {
        var resolutions = tileSource.Schema.Resolutions;
        minZoom = int.MaxValue;
        maxZoom = int.MinValue;
        foreach (var kv in resolutions)
        {
            int z = kv.Key; // already int in BruTile 6.0
            if (z < minZoom) minZoom = z;
            if (z > maxZoom) maxZoom = z;
        }
        if (minZoom == int.MaxValue) minZoom = 0;
        if (maxZoom == int.MinValue) maxZoom = 0;
    }

    /// <summary>Return the attribution text for a tile source (if available).</summary>
    public static string TileAttribution(ITileSource tileSource)
        => tileSource.Attribution.Text ?? string.Empty;
}
