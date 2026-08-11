using System;
using VL.Core.Import;

namespace VL.GIS.Skia;

/// <summary>
/// Map viewport: what is on screen, and the conversion between geographic coordinates and
/// screen pixels.
/// </summary>
[Name("Viewport")]
public static class ViewportNodes
{
    // Web Mercator wraps the globe onto a square, so latitude has to stop short of the
    // poles. This is the standard cut-off, chosen so the square is exactly square.
    private const double MaxLatitude = 85.0511287798066;

    /// <summary>
    /// Describe what part of the world is on screen: a centre in WGS84, a slippy-map zoom
    /// level, and the size of the view in pixels.
    /// Everything else in GIS.Skia is derived from this.
    /// VL.GIS's own implementation, not an upstream library.
    /// </summary>
    public static MapView CreateMapView(
        double centerLongitude = 0.0,
        double centerLatitude = 0.0,
        double zoom = 2.0,
        float width = 512f,
        float height = 512f)
        => new MapView(centerLongitude, centerLatitude, zoom, width, height);

    /// <summary>
    /// Read a MapView's parts back out.
    /// A record is opaque in a patch, so without this you cannot see what you built.
    /// VL.GIS's own implementation, not an upstream library.
    /// </summary>
    public static void MapViewInfo(
        MapView view,
        out double centerLongitude,
        out double centerLatitude,
        out double zoom,
        out float width,
        out float height)
    {
        centerLongitude = view.CenterLongitude;
        centerLatitude  = view.CenterLatitude;
        zoom            = view.Zoom;
        width           = view.Width;
        height          = view.Height;
    }

    /// <summary>
    /// Ground distance covered by one pixel, in metres, at the view's centre latitude.
    /// Useful for scale bars, and for deciding how much detail is worth drawing.
    /// VL.GIS's own implementation, not an upstream library.
    /// </summary>
    public static double Resolution(MapView view)
    {
        const double EquatorMetres = 2.0 * Math.PI * 6378137.0;
        return EquatorMetres / view.WorldSize
             * Math.Cos(Clamp(view.CenterLatitude) * Math.PI / 180.0);
    }

    // ── World pixels ──────────────────────────────────────────────────────────
    //
    // The intermediate space everything goes through: the whole world as a square of
    // WorldSize pixels, origin at the top-left (180°W, ~85°N). Tile (col, row) occupies
    // exactly (col*256, row*256) to (col*256+256, row*256+256) at integer zoom, which is
    // what makes tile placement fall out for free.

    internal static (double x, double y) LonLatToWorld(double longitude, double latitude, double worldSize)
    {
        double lat = Clamp(latitude);
        double x = (longitude + 180.0) / 360.0 * worldSize;
        double sin = Math.Sin(lat * Math.PI / 180.0);
        double y = (0.5 - Math.Log((1.0 + sin) / (1.0 - sin)) / (4.0 * Math.PI)) * worldSize;
        return (x, y);
    }

    internal static (double longitude, double latitude) WorldToLonLat(double x, double y, double worldSize)
    {
        double longitude = x / worldSize * 360.0 - 180.0;
        double n = Math.PI - 2.0 * Math.PI * y / worldSize;
        double latitude = 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
        return (longitude, latitude);
    }

    private static double Clamp(double latitude)
        => Math.Max(-MaxLatitude, Math.Min(MaxLatitude, latitude));

    // ── Screen ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Where a WGS84 coordinate lands on screen, in pixels from the top-left of the view.
    /// Results outside 0..Width / 0..Height are off screen, which is not an error.
    /// VL.GIS's own implementation, not an upstream library.
    /// </summary>
    public static (float x, float y) LonLatToScreen(MapView view, double longitude, double latitude)
    {
        var (wx, wy) = LonLatToWorld(longitude, latitude, view.WorldSize);
        var (cx, cy) = LonLatToWorld(view.CenterLongitude, view.CenterLatitude, view.WorldSize);
        return ((float)(wx - cx + view.Width / 2.0), (float)(wy - cy + view.Height / 2.0));
    }

    /// <summary>
    /// The WGS84 coordinate under a screen pixel. The inverse of LonLatToScreen, and what
    /// turns a mouse position into a place.
    /// VL.GIS's own implementation, not an upstream library.
    /// </summary>
    public static (double longitude, double latitude) ScreenToLonLat(MapView view, float x, float y)
    {
        var (cx, cy) = LonLatToWorld(view.CenterLongitude, view.CenterLatitude, view.WorldSize);
        return WorldToLonLat(cx + x - view.Width / 2.0, cy + y - view.Height / 2.0, view.WorldSize);
    }

    /// <summary>
    /// The geographic bounds currently on screen, as SW and NE corners.
    /// Feed these to GIS.Tiles TileIndicesForBounds to learn which tiles to fetch.
    /// VL.GIS's own implementation, not an upstream library.
    /// </summary>
    public static void ViewBounds(
        MapView view,
        out double minLongitude, out double minLatitude,
        out double maxLongitude, out double maxLatitude)
    {
        // Screen y grows downward while latitude grows upward, so the top-left pixel gives
        // the maximum latitude and the bottom-right the minimum.
        var (westLon, northLat) = ScreenToLonLat(view, 0f, 0f);
        var (eastLon, southLat) = ScreenToLonLat(view, view.Width, view.Height);

        minLongitude = westLon;
        maxLongitude = eastLon;
        minLatitude  = southLat;
        maxLatitude  = northLat;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Move the view by a pixel offset, the way dragging a map does.
    /// VL.GIS's own implementation, not an upstream library.
    /// </summary>
    public static MapView PanByPixels(MapView view, float deltaX, float deltaY)
    {
        var (cx, cy) = LonLatToWorld(view.CenterLongitude, view.CenterLatitude, view.WorldSize);
        var (lon, lat) = WorldToLonLat(cx - deltaX, cy - deltaY, view.WorldSize);
        return view with { CenterLongitude = lon, CenterLatitude = lat };
    }

    /// <summary>
    /// Zoom by a number of levels while holding one screen pixel still — what a scroll
    /// wheel over a map does. Pass the view centre to zoom centrally.
    /// VL.GIS's own implementation, not an upstream library.
    /// </summary>
    public static MapView ZoomAround(MapView view, float screenX, float screenY, double deltaZoom)
    {
        var (anchorLon, anchorLat) = ScreenToLonLat(view, screenX, screenY);
        var zoomed = view with { Zoom = view.Zoom + deltaZoom };

        // Put the anchor back under the same pixel by shifting the centre by however far it
        // moved at the new scale.
        var (nx, ny) = LonLatToScreen(zoomed, anchorLon, anchorLat);
        return PanByPixels(zoomed, screenX - nx, screenY - ny);
    }
}
