using System;

namespace VL.GIS.Skia;

/// <summary>
/// What part of the world is on screen, and how big the screen is.
/// </summary>
/// <remarks>
/// Everything else in GIS.Skia is a function of this: which tiles to fetch, where to put
/// them, and where a geometry's coordinates land in pixels.
///
/// Zoom is the slippy-map zoom level and may be fractional. At zoom z the whole world is
/// 256 * 2^z pixels wide, which is what makes a tile exactly 256 pixels when the zoom is a
/// whole number -- fetch tiles at Floor(zoom) and they will be drawn at their natural size.
/// </remarks>
public readonly record struct MapView(
    double CenterLongitude,
    double CenterLatitude,
    double Zoom,
    float Width,
    float Height)
{
    /// <summary>Width of the whole world in pixels at this zoom.</summary>
    internal double WorldSize => TileSize * Math.Pow(2.0, Zoom);

    internal const double TileSize = 256.0;
}
