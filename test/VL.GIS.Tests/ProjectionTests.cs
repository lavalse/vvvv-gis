using NetTopologySuite.Geometries;
using VL.GIS;

namespace VL.GIS.Tests;

public class ProjectionTests
{
    const double Lon = 139.7;
    const double Lat = 35.68;

    static double NGonArea(double radius, int sides)
        => 0.5 * sides * radius * radius * Math.Sin(2.0 * Math.PI / sides);

    [Theory]
    [InlineData(139.7, 54)]   // Tokyo
    [InlineData(-180.0, 1)]   // western edge of zone 1
    [InlineData(-174.1, 1)]   // still zone 1
    [InlineData(-174.0, 2)]   // first longitude in zone 2
    [InlineData(0.0, 31)]     // Greenwich
    [InlineData(179.9, 60)]   // last zone
    public void UtmZoneFromLongitude_matches_the_standard_zones(double longitude, int zone)
    {
        Assert.Equal(zone, ProjectionNodes.UtmZoneFromLongitude(longitude));
    }

    [Fact]
    public void UtmZoneFromLongitude_wraps_at_the_antimeridian()
    {
        // +180 and -180 are the same meridian, so both land in zone 1. This is what the
        // "% 60" in the implementation is for; without it the answer would be 61.
        Assert.Equal(1, ProjectionNodes.UtmZoneFromLongitude(180.0));
        Assert.Equal(ProjectionNodes.UtmZoneFromLongitude(-180.0),
                     ProjectionNodes.UtmZoneFromLongitude(180.0));
    }

    [Fact]
    public void ReprojectGeometry_round_trips_through_UTM()
    {
        var wgs84 = ProjectionNodes.Wgs84();
        var utm   = ProjectionNodes.CreateUtm(54, true);
        var point = GeometryNodes.CreatePoint(Lon, Lat);

        var projected = ProjectionNodes.ReprojectGeometry(point, wgs84, utm);
        var back      = ProjectionNodes.ReprojectGeometry(projected, utm, wgs84);

        Assert.Equal(Lon, back.Coordinates[0].X, 9);
        Assert.Equal(Lat, back.Coordinates[0].Y, 9);
    }

    [Fact]
    public void UTM_easting_is_within_the_zone_and_northing_matches_the_hemisphere()
    {
        // UTM gives the central meridian a false easting of 500 km, and a zone is 6 degrees
        // wide, so any point in the zone lands roughly within 500 km +/- 340 km. Northing
        // is metres from the equator. Both are coarse checks, but they would catch a
        // transform that silently produced degrees, or the southern-hemisphere variant.
        var projected = ProjectionNodes.ReprojectGeometry(
            GeometryNodes.CreatePoint(Lon, Lat),
            ProjectionNodes.Wgs84(),
            ProjectionNodes.CreateUtm(54, true));

        double easting  = projected.Coordinates[0].X;
        double northing = projected.Coordinates[0].Y;

        Assert.InRange(easting, 160_000, 840_000);
        Assert.InRange(northing, 3_900_000, 4_000_000);   // ~35.7 degrees north
    }

    [Fact]
    public void Buffer_in_UTM_is_in_metres()
    {
        // The correct half of help\HowTo Buffer in metres.vl, pinned to the closed form
        // rather than to the number that happened to appear in the IOBox.
        var projected = ProjectionNodes.ReprojectGeometry(
            GeometryNodes.CreatePoint(Lon, Lat),
            ProjectionNodes.Wgs84(),
            ProjectionNodes.CreateUtm(54, true));

        double area = GeometryNodes.Area(GeometryNodes.Buffer(projected, 1000));

        Assert.Equal(NGonArea(1000, 64), area, 2);
    }

    [Fact]
    public void Web_Mercator_is_not_equal_area_so_metric_buffering_there_is_wrong()
    {
        // Worth stating as a test because reprojecting to EPSG:3857 for "metric" work is
        // the obvious move and it is wrong by 1/cos(latitude) -- about 23% at Tokyo. UTM is
        // the right choice; this asserts the size of the mistake you avoid by using it.
        var point = GeometryNodes.CreatePoint(Lon, Lat);

        var mercator = ProjectionNodes.ReprojectGeometry(
            point, ProjectionNodes.Wgs84(), ProjectionNodes.WebMercator());
        var utm = ProjectionNodes.ReprojectGeometry(
            point, ProjectionNodes.Wgs84(), ProjectionNodes.CreateUtm(54, true));

        double mercatorArea = GeometryNodes.Area(GeometryNodes.Buffer(mercator, 1000));
        double utmArea      = GeometryNodes.Area(GeometryNodes.Buffer(utm, 1000));

        // Both buffers are the same 64-gon in their own units; the areas are equal as
        // numbers. The distortion is in what a Web Mercator metre means on the ground.
        Assert.Equal(utmArea, mercatorArea, 2);

        // A Web Mercator "1000 m" radius at this latitude covers cos(lat) * 1000 m of
        // actual ground, so the true area is smaller by cos(lat) squared.
        double cosLat = Math.Cos(Lat * Math.PI / 180.0);
        double trueGroundArea = mercatorArea * cosLat * cosLat;

        Assert.InRange(trueGroundArea / utmArea, 0.65, 0.67);
    }

    [Fact]
    public void LonLatToWebMercator_round_trips()
    {
        var (x, y) = ProjectionNodes.LonLatToWebMercator(Lon, Lat);
        var (lon, lat) = ProjectionNodes.WebMercatorToLonLat(x, y);

        Assert.Equal(Lon, lon, 9);
        Assert.Equal(Lat, lat, 9);
    }

    [Fact]
    public void LonLatToWebMercator_agrees_with_ProjNet()
    {
        // Two independent implementations of the same projection: the closed-form formulas
        // in LonLatToWebMercator, and ProjNet's EPSG:3857. Agreement to a millimetre means
        // neither has a sign or radius error.
        var (x, y) = ProjectionNodes.LonLatToWebMercator(Lon, Lat);

        var viaProjNet = ProjectionNodes.ReprojectGeometry(
            GeometryNodes.CreatePoint(Lon, Lat),
            ProjectionNodes.Wgs84(),
            ProjectionNodes.WebMercator());

        Assert.Equal(x, viaProjNet.Coordinates[0].X, 3);
        Assert.Equal(y, viaProjNet.Coordinates[0].Y, 3);
    }

    [Fact]
    public void Web_Mercator_origin_is_the_intersection_of_equator_and_Greenwich()
    {
        var (x, y) = ProjectionNodes.LonLatToWebMercator(0, 0);

        // Not exactly zero, and the reason is worth recording: the projection evaluates
        // log(tan(pi/4 + lat/2)), and tan(pi/4) in double precision is 0.9999999999999999,
        // whose log is -1.1e-16. Multiplied by the earth's radius that lands at -7.1e-10
        // metres. Sub-nanometre error means the formula is analytically right; a tolerance
        // of a micrometre keeps the test about the projection instead of about IEEE 754.
        Assert.InRange(Math.Abs(x), 0.0, 1e-6);
        Assert.InRange(Math.Abs(y), 0.0, 1e-6);
    }

    [Fact]
    public void ReprojectPoints_agrees_with_reprojecting_one_at_a_time()
    {
        var transform = ProjectionNodes.CreateTransformation(
            ProjectionNodes.Wgs84(), ProjectionNodes.CreateUtm(54, true));

        var input = new[] { (Lon, Lat), (139.8, 35.7), (139.6, 35.6) };
        var bulk  = ProjectionNodes.ReprojectPoints(transform, input).ToList();

        Assert.Equal(input.Length, bulk.Count);

        for (int i = 0; i < input.Length; i++)
        {
            var single = ProjectionNodes.ReprojectPoint(transform, input[i].Item1, input[i].Item2);
            Assert.Equal(single.x, bulk[i].x, 6);
            Assert.Equal(single.y, bulk[i].y, 6);
        }
    }
}
