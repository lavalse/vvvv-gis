using NetTopologySuite.Geometries;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using System;
using System.Collections.Generic;
using VL.Core.Import;

namespace VL.GIS;

/// <summary>
/// Coordinate reference system (CRS) and reprojection nodes.
/// </summary>
[Name("Projection")]
public static class ProjectionNodes
{
    private static readonly CoordinateSystemFactory CsFactory = new CoordinateSystemFactory();
    private static readonly CoordinateTransformationFactory CtFactory = new CoordinateTransformationFactory();

    // Well-known CRS instances
    /// <summary>WGS84 geographic CRS (EPSG:4326) — longitude/latitude in degrees.</summary>
    public static CoordinateSystem Wgs84() => GeographicCoordinateSystem.WGS84;

    /// <summary>Web Mercator projected CRS (EPSG:3857) — used by OSM/Google/Bing tiles.</summary>
    public static CoordinateSystem WebMercator()
        => ProjectedCoordinateSystem.WebMercator;

    // ── CRS Parsing ───────────────────────────────────────────────────────────

    /// <summary>Parse a CRS from a WKT (Well-Known Text) string.</summary>
    public static CoordinateSystem ParseWkt(string wkt)
        => CsFactory.CreateFromWkt(wkt);

    /// <summary>
    /// Read a coordinate system's name, authority and WKT — for example
    /// "WGS 84 / UTM zone 54N", "EPSG", 32654.
    /// Use it to confirm in a patch which CRS you actually built.
    /// </summary>
    public static void CoordinateSystemInfo(
        CoordinateSystem coordinateSystem,
        out string name,
        out string authority,
        out int authorityCode,
        out string wkt)
    {
        // A CoordinateSystem is opaque in a patch: an IOBox can only show the type name, so
        // until this node existed there was no way to check that CreateUtm(54) had produced
        // the zone you meant. The help patch for metric buffering had to infer it from the
        // resulting area.
        name          = coordinateSystem.Name ?? string.Empty;
        authority     = coordinateSystem.Authority ?? string.Empty;
        authorityCode = GetSrid(coordinateSystem);
        wkt           = coordinateSystem.WKT ?? string.Empty;
    }

    /// <summary>
    /// Create a UTM projected CRS for a given zone number and hemisphere.
    /// Useful for metric distance/area calculations.
    /// </summary>
    public static CoordinateSystem CreateUtm(int zoneNumber, bool isNorthernHemisphere = true)
        => ProjectedCoordinateSystem.WGS84_UTM(zoneNumber, isNorthernHemisphere);

    /// <summary>
    /// Determine the UTM zone number for a given longitude.
    /// Returns zone 1–60.
    /// </summary>
    public static int UtmZoneFromLongitude(double longitude)
        => (int)Math.Floor((longitude + 180.0) / 6.0) % 60 + 1;

    // ── Reprojection ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create a coordinate transformation between two CRS definitions.
    /// Cache and reuse this for bulk reprojection.
    /// </summary>
    public static ICoordinateTransformation CreateTransformation(
        CoordinateSystem source,
        CoordinateSystem target)
        => CtFactory.CreateFromCoordinateSystems(source, target);

    /// <summary>Reproject a single (x, y) coordinate using a pre-built transformation.</summary>
    public static (double x, double y) ReprojectPoint(
        ICoordinateTransformation transformation,
        double x, double y)
    {
        double[] pt = transformation.MathTransform.Transform(new[] { x, y });
        return (pt[0], pt[1]);
    }

    /// <summary>
    /// Reproject a Point geometry from source CRS to target CRS.
    /// Input point coordinates must be in the source CRS.
    /// </summary>
    public static Point ReprojectPointGeometry(
        Point point,
        CoordinateSystem source,
        CoordinateSystem target)
    {
        var transform = CtFactory.CreateFromCoordinateSystems(source, target);
        double[] pt = transform.MathTransform.Transform(new[] { point.X, point.Y });
        var factory = new GeometryFactory(new PrecisionModel(), GetSrid(target));
        return factory.CreatePoint(new Coordinate(pt[0], pt[1]));
    }

    /// <summary>
    /// Reproject any NTS Geometry from source CRS to target CRS.
    /// Uses coordinate-by-coordinate transformation.
    /// </summary>
    public static Geometry ReprojectGeometry(
        Geometry geometry,
        CoordinateSystem source,
        CoordinateSystem target)
    {
        var transform = CtFactory.CreateFromCoordinateSystems(source, target);
        var filter = new CoordinateTransformFilter(transform.MathTransform);
        var clone = geometry.Copy();
        clone.Apply(filter);
        clone.GeometryChanged();

        // Copy() carries the source SRID across, so without this the result holds target-CRS
        // coordinates while still declaring the CRS it came from -- right numbers, wrong
        // label, and nothing downstream can tell. ReprojectPointGeometry above has always
        // done this; the two nodes disagreeing was the worse half of the bug, since which
        // one you picked decided whether your data was labelled correctly.
        clone.SRID = GetSrid(target);
        return clone;
    }

    /// <summary>
    /// Reproject a list of (x, y) coordinates in bulk.
    /// More efficient than calling ReprojectPoint repeatedly.
    /// </summary>
    public static IReadOnlyList<(double x, double y)> ReprojectPoints(
        ICoordinateTransformation transformation,
        IEnumerable<(double x, double y)> points)
    {
        var result = new List<(double, double)>();
        foreach (var (x, y) in points)
        {
            double[] pt = transformation.MathTransform.Transform(new[] { x, y });
            result.Add((pt[0], pt[1]));
        }
        return result;
    }

    // ── Web Mercator Utilities ────────────────────────────────────────────────

    /// <summary>Convert WGS84 longitude/latitude to Web Mercator (EPSG:3857) meters.</summary>
    public static (double x, double y) LonLatToWebMercator(double longitude, double latitude)
    {
        const double R = 6378137.0; // Earth radius in metres (WGS84 semi-major axis)
        double x = longitude * Math.PI / 180.0 * R;
        double y = Math.Log(Math.Tan(Math.PI / 4.0 + latitude * Math.PI / 360.0)) * R;
        return (x, y);
    }

    /// <summary>Convert Web Mercator (EPSG:3857) meters to WGS84 longitude/latitude.</summary>
    public static (double longitude, double latitude) WebMercatorToLonLat(double x, double y)
    {
        const double R = 6378137.0;
        double longitude = x / R * 180.0 / Math.PI;
        double latitude = (2.0 * Math.Atan(Math.Exp(y / R)) - Math.PI / 2.0) * 180.0 / Math.PI;
        return (longitude, latitude);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The CRS's EPSG code, or 0 if it does not declare one. AuthorityCode is declared on
    /// CoordinateSystem's base class, so no type test is needed; the earlier version checked
    /// for ProjectedCoordinateSystem and GeographicCoordinateSystem separately and would
    /// silently have returned 0 for any third kind.
    /// </summary>
    private static int GetSrid(CoordinateSystem cs)
        => cs.AuthorityCode > 0 ? (int)cs.AuthorityCode : 0;

    /// <summary>NTS coordinate filter that applies a ProjNet math transform.</summary>
    private sealed class CoordinateTransformFilter : ICoordinateSequenceFilter
    {
        private readonly MathTransform _transform;

        public CoordinateTransformFilter(MathTransform transform) => _transform = transform;

        public bool Done => false;
        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq, int i)
        {
            double[] pt = _transform.Transform(new[] { seq.GetX(i), seq.GetY(i) });
            seq.SetX(i, pt[0]);
            seq.SetY(i, pt[1]);
        }
    }
}
