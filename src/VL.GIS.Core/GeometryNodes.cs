using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using System.Collections.Generic;
using System.Linq;

namespace VL.GIS.Core;

/// <summary>
/// Geometry creation and spatial operation nodes for vvvv.
/// Every public static method becomes a node in the vvvv NodeBrowser.
/// Category: GIS.Geometry
/// </summary>
public static class GeometryNodes
{
    private static readonly GeometryFactory Factory = new GeometryFactory(new PrecisionModel(), 4326);

    // ── Creation ──────────────────────────────────────────────────────────────

    /// <summary>Create a Point geometry from longitude/latitude (WGS84).</summary>
    public static Point CreatePoint(double longitude, double latitude)
        => Factory.CreatePoint(new Coordinate(longitude, latitude));

    /// <summary>Create a Point with explicit Z (elevation in metres).</summary>
    public static Point CreatePoint3D(double longitude, double latitude, double elevation)
        => Factory.CreatePoint(new CoordinateZ(longitude, latitude, elevation));

    /// <summary>Create a LineString from an ordered sequence of coordinates.</summary>
    public static LineString CreateLineString(IEnumerable<(double longitude, double latitude)> points)
    {
        var coords = points.Select(p => new Coordinate(p.longitude, p.latitude)).ToArray();
        return Factory.CreateLineString(coords);
    }

    /// <summary>Create a Polygon from an exterior ring. Ring is auto-closed.</summary>
    public static Polygon CreatePolygon(IEnumerable<(double longitude, double latitude)> exteriorRing)
    {
        var coords = exteriorRing.Select(p => new Coordinate(p.longitude, p.latitude)).ToList();
        // Auto-close ring
        if (coords.Count > 0 && !coords[0].Equals2D(coords[^1]))
            coords.Add(coords[0]);
        return Factory.CreatePolygon(coords.ToArray());
    }

    /// <summary>Create a Polygon with exterior ring and interior holes.</summary>
    public static Polygon CreatePolygonWithHoles(
        IEnumerable<(double longitude, double latitude)> exteriorRing,
        IEnumerable<IEnumerable<(double longitude, double latitude)>> holes)
    {
        var shell = ToLinearRing(exteriorRing);
        var holeRings = holes.Select(ToLinearRing).ToArray();
        return Factory.CreatePolygon(shell, holeRings);
    }

    /// <summary>Create a bounding box polygon from min/max lon/lat.</summary>
    public static Polygon CreateBoundingBox(
        double minLongitude, double minLatitude,
        double maxLongitude, double maxLatitude)
    {
        return Factory.CreatePolygon(new[]
        {
            new Coordinate(minLongitude, minLatitude),
            new Coordinate(maxLongitude, minLatitude),
            new Coordinate(maxLongitude, maxLatitude),
            new Coordinate(minLongitude, maxLatitude),
            new Coordinate(minLongitude, minLatitude),
        });
    }

    // ── Spatial Operations ────────────────────────────────────────────────────

    /// <summary>Buffer a geometry by a given distance (in the geometry's CRS units).</summary>
    public static Geometry Buffer(Geometry geometry, double distance, int segments = 16)
        => geometry.Buffer(distance, segments);

    /// <summary>Buffer using flat/round/square end cap style.</summary>
    public static Geometry BufferWithStyle(
        Geometry geometry,
        double distance,
        EndCapStyle endCapStyle = EndCapStyle.Round,
        int segments = 16)
    {
        var parameters = new BufferParameters(segments, endCapStyle);
        return BufferOp.Buffer(geometry, distance, parameters);
    }

    /// <summary>Compute the intersection of two geometries.</summary>
    public static Geometry Intersection(Geometry a, Geometry b) => a.Intersection(b);

    /// <summary>Compute the union of two geometries.</summary>
    public static Geometry Union(Geometry a, Geometry b) => a.Union(b);

    /// <summary>Compute the difference: A minus B.</summary>
    public static Geometry Difference(Geometry a, Geometry b) => a.Difference(b);

    /// <summary>Compute the symmetric difference (XOR) of two geometries.</summary>
    public static Geometry SymmetricDifference(Geometry a, Geometry b) => a.SymmetricDifference(b);

    /// <summary>Compute the convex hull of a geometry.</summary>
    public static Geometry ConvexHull(Geometry geometry) => geometry.ConvexHull();

    /// <summary>Return the centroid of a geometry.</summary>
    public static Point Centroid(Geometry geometry) => geometry.Centroid;

    /// <summary>Return the envelope (bounding box) of a geometry.</summary>
    public static Geometry Envelope(Geometry geometry) => geometry.Envelope;

    /// <summary>Simplify geometry using Douglas-Peucker with given tolerance.</summary>
    public static Geometry Simplify(Geometry geometry, double distanceTolerance)
        => NetTopologySuite.Simplify.DouglasPeuckerSimplifier.Simplify(geometry, distanceTolerance);

    // ── Predicates ────────────────────────────────────────────────────────────

    /// <summary>Test whether geometry A intersects geometry B.</summary>
    public static bool Intersects(Geometry a, Geometry b) => a.Intersects(b);

    /// <summary>Test whether geometry A contains geometry B.</summary>
    public static bool Contains(Geometry a, Geometry b) => a.Contains(b);

    /// <summary>Test whether geometry A is within geometry B.</summary>
    public static bool Within(Geometry a, Geometry b) => a.Within(b);

    /// <summary>Test whether geometry A touches geometry B.</summary>
    public static bool Touches(Geometry a, Geometry b) => a.Touches(b);

    /// <summary>Test whether geometry A is disjoint from geometry B.</summary>
    public static bool Disjoint(Geometry a, Geometry b) => a.Disjoint(b);

    /// <summary>Test whether geometry A covers geometry B.</summary>
    public static bool Covers(Geometry a, Geometry b) => a.Covers(b);

    // ── Measurements ─────────────────────────────────────────────────────────

    /// <summary>Return the area of a geometry (in CRS units squared).</summary>
    public static double Area(Geometry geometry) => geometry.Area;

    /// <summary>Return the length/perimeter of a geometry (in CRS units).</summary>
    public static double Length(Geometry geometry) => geometry.Length;

    /// <summary>Return the minimum distance between two geometries (in CRS units).</summary>
    public static double Distance(Geometry a, Geometry b) => a.Distance(b);

    /// <summary>Decompose a Geometry into its component geometries.</summary>
    public static IReadOnlyList<Geometry> GetGeometries(Geometry geometry)
    {
        var result = new List<Geometry>(geometry.NumGeometries);
        for (int i = 0; i < geometry.NumGeometries; i++)
            result.Add(geometry.GetGeometryN(i));
        return result;
    }

    /// <summary>Get all coordinates of a geometry as (longitude, latitude) tuples.</summary>
    public static IReadOnlyList<(double longitude, double latitude)> GetCoordinates(Geometry geometry)
        => geometry.Coordinates.Select(c => (c.X, c.Y)).ToList();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LinearRing ToLinearRing(IEnumerable<(double longitude, double latitude)> points)
    {
        var coords = points.Select(p => new Coordinate(p.longitude, p.latitude)).ToList();
        if (coords.Count > 0 && !coords[0].Equals2D(coords[^1]))
            coords.Add(coords[0]);
        return Factory.CreateLinearRing(coords.ToArray());
    }
}
