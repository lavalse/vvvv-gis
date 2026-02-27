using BruTile;
using BruTile.Cache;
using BruTile.Predefined;
using BruTile.Web;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace VL.GIS.Tiles;

/// <summary>
/// Map tile provider factory nodes for vvvv.
/// Creates ITileSource instances for various tile services.
/// Category: GIS.Tiles
/// </summary>
public static class TileProviderNodes
{
    // Shared HttpClient for all web tile sources
    private static readonly HttpClient SharedHttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "VL.GIS/0.1 (vvvv gamma; https://vvvv.org)" } }
    };

    // ── OSM / OpenStreetMap ───────────────────────────────────────────────────

    /// <summary>
    /// Create an OpenStreetMap tile source (standard tile.openstreetmap.org).
    /// Returns tiles at zoom levels 0–19, 256×256 px, Web Mercator (EPSG:3857).
    /// </summary>
    public static ITileSource OsmTileSource()
        => new HttpTileSource(
            new GlobalSphericalMercator(0, 19),
            "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            name: "OpenStreetMap",
            configureHttpRequestMessage: msg =>
                msg.Headers.Add("User-Agent", "VL.GIS/0.1 (vvvv gamma)"));

    /// <summary>
    /// Create an OpenTopoMap tile source — topographic rendering of OSM data.
    /// </summary>
    public static ITileSource OpenTopoMapTileSource()
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
    public static ITileSource XyzTileSource(
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

    // ── WMTS ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a WMTS tile source from a capabilities URL.
    /// Automatically parses the WMTS GetCapabilities response.
    /// </summary>
    public static ITileSource WmtsTileSource(
        string capabilitiesUrl,
        string layerIdentifier)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "VL.GIS/0.1 (vvvv gamma)");
        var capabilitiesData = client.GetByteArrayAsync(capabilitiesUrl).GetAwaiter().GetResult();
        var tileSources = BruTile.Wmts.WmtsParser.Parse(capabilitiesData);
        foreach (var source in tileSources)
        {
            if (source.Name?.Contains(layerIdentifier, StringComparison.OrdinalIgnoreCase) == true)
                return source;
        }
        // Return first available if no match
        foreach (var source in tileSources)
            return source;
        throw new InvalidOperationException(
            $"No WMTS layer found for identifier '{layerIdentifier}' at {capabilitiesUrl}");
    }

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
        minZoom = 0;
        maxZoom = 0;
        foreach (var kv in resolutions)
        {
            if (int.TryParse(kv.Key, out int z))
            {
                if (z < minZoom) minZoom = z;
                if (z > maxZoom) maxZoom = z;
            }
        }
    }

    /// <summary>Return the attribution text for a tile source (if available).</summary>
    public static string TileAttribution(ITileSource tileSource)
        => tileSource.Attribution?.Text ?? string.Empty;
}
