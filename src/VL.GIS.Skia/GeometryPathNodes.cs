using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using SkiaSharp;
using VL.Core.Import;

namespace VL.GIS.Skia;

/// <summary>
/// Turns geographic geometry into Skia paths positioned for a given view.
/// </summary>
/// <remarks>
/// The result goes to VL.Skia's PathLayer or DrawPath, so styling — stroke, fill, colour —
/// stays where a vvvv user expects it rather than being decided here.
/// </remarks>
[Name("Paths")]
public static class GeometryPathNodes
{
    /// <summary>
    /// Project a geometry into screen space and return it as a path ready to draw.
    /// Handles points, lines, polygons (including holes) and any collection of them.
    /// Coordinates are assumed to be WGS84 longitude/latitude.
    /// Uses NetTopologySuite for the geometry model; SKPath is SkiaSharp's.
    /// </summary>
    public static SKPath GeometryToPath(MapView view, Geometry? geometry, float pointRadius = 4f)
    {
        var path = new SKPath();
        if (geometry is null || geometry.IsEmpty) return path;
        Append(path, view, geometry, pointRadius);
        return path;
    }

    /// <summary>
    /// Project several geometries into one path. Cheaper to draw than one path each when
    /// they share a style.
    /// Uses NetTopologySuite for the geometry model; SKPath is SkiaSharp's.
    /// </summary>
    public static SKPath GeometriesToPath(
        MapView view, IEnumerable<Geometry>? geometries, float pointRadius = 4f)
    {
        var path = new SKPath();
        if (geometries is null) return path;
        foreach (var g in geometries)
            if (g is not null && !g.IsEmpty)
                Append(path, view, g, pointRadius);
        return path;
    }

    private static void Append(SKPath path, MapView view, Geometry geometry, float pointRadius)
    {
        switch (geometry)
        {
            case Point point:
                // A point has no extent, so it needs a shape to be visible at all. A circle
                // keeps it round at any zoom, unlike a projected ground radius.
                var (px, py) = ViewportNodes.LonLatToScreen(view, point.X, point.Y);
                path.AddCircle(px, py, pointRadius);
                break;

            case LineString line:
                AppendRing(path, view, line.Coordinates, close: false);
                break;

            case Polygon polygon:
                // Holes have to wind opposite to the shell, because SKPath fills by the
                // non-zero winding rule: two contours turning the same way simply nest as
                // solid, and the hole never appears. NTS does not guarantee any particular
                // orientation, so normalise rather than hope.
                //
                // Even-odd fill would punch the hole without this, but it would also punch a
                // hole wherever two polygons overlap in GeometriesToPath, which is worse.
                AppendRing(path, view, Oriented(polygon.ExteriorRing, counterClockwise: true), close: true);
                foreach (var hole in polygon.InteriorRings)
                    AppendRing(path, view, Oriented(hole, counterClockwise: false), close: true);
                break;

            case GeometryCollection collection:
                foreach (var part in collection.Geometries)
                    Append(path, view, part, pointRadius);
                break;

            default:
                // MultiPoint, MultiLineString and MultiPolygon are all GeometryCollections,
                // so anything reaching here is a type NTS gained after this was written.
                // Falling back to the coordinates draws something rather than nothing.
                AppendRing(path, view, geometry.Coordinates, close: false);
                break;
        }
    }

    /// <summary>
    /// The ring's coordinates, reversed if it does not already turn the requested way.
    /// </summary>
    private static Coordinate[] Oriented(LineString ring, bool counterClockwise)
    {
        var coordinates = ring.Coordinates;
        if (coordinates.Length < 4) return coordinates;   // not a closed ring; leave it alone

        // Screen y is flipped relative to latitude, so a ring that is counter-clockwise on
        // the ground draws clockwise. That inverts both contours equally and the shell/hole
        // relationship survives, which is all the winding rule cares about.
        bool isCCW = NetTopologySuite.Algorithm.Orientation.IsCCW(coordinates);
        if (isCCW == counterClockwise) return coordinates;

        var reversed = new Coordinate[coordinates.Length];
        for (int i = 0; i < coordinates.Length; i++)
            reversed[i] = coordinates[coordinates.Length - 1 - i];
        return reversed;
    }

    private static void AppendRing(SKPath path, MapView view, Coordinate[] coordinates, bool close)
    {
        if (coordinates.Length == 0) return;

        for (int i = 0; i < coordinates.Length; i++)
        {
            var (x, y) = ViewportNodes.LonLatToScreen(view, coordinates[i].X, coordinates[i].Y);
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }

        // NTS closes its rings by repeating the first coordinate, so the contour is already
        // geometrically closed; Close() marks it as such for Skia's fill and joins.
        if (close) path.Close();
    }
}
