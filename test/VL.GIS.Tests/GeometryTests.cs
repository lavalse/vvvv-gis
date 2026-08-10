using NetTopologySuite.Geometries;
using VL.GIS;

namespace VL.GIS.Tests;

public class GeometryTests
{
    // Tokyo. The same coordinates the help patches use, so a failure here and a wrong
    // reading in help\HowTo Create a point.vl point at the same cause.
    const double Lon = 139.7;
    const double Lat = 35.68;

    /// <summary>
    /// Area of a regular n-gon inscribed in a circle of the given radius. Buffer produces
    /// one of these rather than a circle, so this -- not pi*r*r -- is the right expectation.
    /// </summary>
    static double NGonArea(double radius, int sides)
        => 0.5 * sides * radius * radius * Math.Sin(2.0 * Math.PI / sides);

    [Fact]
    public void CreatePoint_puts_longitude_in_X()
    {
        // The single most consequential convention in the library. Swapping these is silent:
        // (35.68, 139.7) is a valid coordinate pair, it just isn't Tokyo, and every
        // downstream node keeps working.
        var p = GeometryNodes.CreatePoint(Lon, Lat);

        Assert.Equal(Lon, p.X);
        Assert.Equal(Lat, p.Y);
    }

    [Fact]
    public void CreatePoint_carries_the_WGS84_SRID()
    {
        Assert.Equal(4326, GeometryNodes.CreatePoint(Lon, Lat).SRID);
    }

    [Fact]
    public void Buffer_defaults_to_16_segments_per_quadrant()
    {
        // 16 per quadrant is 64 sides in total, which is where the 0.16% shortfall against
        // a true circle comes from. Asserted so the default cannot drift unnoticed: it
        // would change every area and length this library reports.
        var buffered = GeometryNodes.Buffer(GeometryNodes.CreatePoint(Lon, Lat), 0.001);

        // A closed ring repeats its first point, hence 64 + 1.
        Assert.Equal(65, buffered.Coordinates.Length);
    }

    [Fact]
    public void Buffer_distance_is_in_CRS_units_not_metres()
    {
        // The trap the second help patch exists to teach. On WGS84 this is a thousandth of
        // a degree, and the area comes back in square degrees -- a number with no physical
        // meaning at all.
        var buffered = GeometryNodes.Buffer(GeometryNodes.CreatePoint(Lon, Lat), 0.001);

        Assert.Equal(NGonArea(0.001, 64), GeometryNodes.Area(buffered), 12);
    }

    [Fact]
    public void Buffer_area_scales_with_the_square_of_the_distance()
    {
        // Degrees and metres differ by a factor of 1e6 in the help patch, and the areas by
        // 1e12. Same shape, same arithmetic, one meaningless answer -- which is the point:
        // nothing about the computation is wrong.
        var p = GeometryNodes.CreatePoint(0, 0);

        double small = GeometryNodes.Area(GeometryNodes.Buffer(p, 0.001));
        double large = GeometryNodes.Area(GeometryNodes.Buffer(p, 1000));

        // Compare the ratio against one rather than against 1e12: asking for absolute
        // decimal places on a number that size is meaningless, and the observed value
        // (999999999999.99939) is correct to fifteen significant figures.
        Assert.Equal(1.0, (large / small) / 1e12, 9);
    }

    [Fact]
    public void Buffer_contains_the_point_it_was_built_from()
    {
        var p = GeometryNodes.CreatePoint(Lon, Lat);
        var buffered = GeometryNodes.Buffer(p, 0.001);

        Assert.True(GeometryNodes.Contains(buffered, p));
        Assert.True(GeometryNodes.Within(p, buffered));
        Assert.True(GeometryNodes.Intersects(buffered, p));
        Assert.False(GeometryNodes.Disjoint(buffered, p));
    }

    [Fact]
    public void Disjoint_buffers_do_not_intersect()
    {
        var a = GeometryNodes.Buffer(GeometryNodes.CreatePoint(0, 0), 0.001);
        var b = GeometryNodes.Buffer(GeometryNodes.CreatePoint(1, 1), 0.001);

        Assert.True(GeometryNodes.Disjoint(a, b));
        Assert.False(GeometryNodes.Intersects(a, b));
        Assert.True(GeometryNodes.Area(GeometryNodes.Intersection(a, b)) == 0.0);
    }

    [Fact]
    public void Union_of_overlapping_buffers_is_smaller_than_the_sum()
    {
        var a = GeometryNodes.Buffer(GeometryNodes.CreatePoint(0, 0), 1);
        var b = GeometryNodes.Buffer(GeometryNodes.CreatePoint(1, 0), 1);

        double union = GeometryNodes.Area(GeometryNodes.Union(a, b));

        Assert.True(union < GeometryNodes.Area(a) + GeometryNodes.Area(b));
        Assert.True(union > GeometryNodes.Area(a));
    }

    [Fact]
    public void Centroid_of_a_symmetric_buffer_is_its_origin()
    {
        var centroid = GeometryNodes.Centroid(
            GeometryNodes.Buffer(GeometryNodes.CreatePoint(Lon, Lat), 0.01));

        Assert.Equal(Lon, centroid.X, 9);
        Assert.Equal(Lat, centroid.Y, 9);
    }

    [Fact]
    public void BoundingBox_of_a_buffer_is_the_distance_either_side()
    {
        var box = GeometryNodes.CreateBoundingBox(139.0, 35.0, 140.0, 36.0);

        Assert.Equal(1.0, GeometryNodes.Area(box), 9);
        Assert.Equal(4.0, GeometryNodes.Length(box), 9);
    }

    [Fact]
    public void Distance_between_points_is_in_degrees_on_WGS84()
    {
        // One degree apart in longitude on the equator reads as 1.0, not 111 km. Same trap
        // as Buffer, and worth pinning separately because Distance looks the most like it
        // ought to return metres.
        double d = GeometryNodes.Distance(
            GeometryNodes.CreatePoint(0, 0),
            GeometryNodes.CreatePoint(1, 0));

        Assert.Equal(1.0, d, 9);
    }

    [Fact]
    public void Simplify_drops_collinear_vertices()
    {
        var line = GeometryNodes.CreateLineString(new[]
        {
            (0.0, 0.0), (0.5, 0.0), (1.0, 0.0)
        });

        var simplified = GeometryNodes.Simplify(line, 0.0001);

        Assert.Equal(3, line.Coordinates.Length);
        Assert.Equal(2, simplified.Coordinates.Length);
    }

    [Fact]
    public void Polygon_ring_is_closed_automatically()
    {
        // Callers pass four corners, not five; NTS requires the ring to close.
        var polygon = GeometryNodes.CreatePolygon(new[]
        {
            (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)
        });

        Assert.Equal(5, polygon.Coordinates.Length);
        Assert.Equal(polygon.Coordinates[0], polygon.Coordinates[^1]);
        Assert.Equal(1.0, GeometryNodes.Area(polygon), 9);
    }

    [Fact]
    public void ConvexHull_of_a_buffer_has_the_same_area()
    {
        var buffered = GeometryNodes.Buffer(GeometryNodes.CreatePoint(Lon, Lat), 0.01);

        Assert.Equal(
            GeometryNodes.Area(buffered),
            GeometryNodes.Area(GeometryNodes.ConvexHull(buffered)),
            12);
    }
}
