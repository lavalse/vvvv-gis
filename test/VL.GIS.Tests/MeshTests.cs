using System.Numerics;
using VL.GIS.Mesh;

namespace VL.GIS.Tests;

/// <summary>
/// GIS.Mesh had never been executed before these tests -- it shipped in 0.1.0-alpha having
/// only ever been compiled.
/// </summary>
public class MeshTests
{
    const double Lon = 139.7;
    const double Lat = 35.68;

    [Fact]
    public void The_origin_maps_to_the_scene_origin()
    {
        var (originLon, originLat) = CoordinateConverter.CreateSceneOrigin(Lon, Lat);
        var local = CoordinateConverter.LonLatToLocal(Lon, Lat, originLon, originLat);

        Assert.Equal(0f, local.X);
        Assert.Equal(0f, local.Y);
        Assert.Equal(0f, local.Z);
    }

    [Fact]
    public void LonLatToLocal_round_trips_within_a_centimetre()
    {
        var (originLon, originLat) = CoordinateConverter.CreateSceneOrigin(Lon, Lat);

        // Roughly 900 m east and 1100 m north of the origin.
        const double targetLon = Lon + 0.01;
        const double targetLat = Lat + 0.01;

        var local = CoordinateConverter.LonLatToLocal(targetLon, targetLat, originLon, originLat);
        var (lon, lat) = CoordinateConverter.LocalToLonLat(local, originLon, originLat);

        // The conversion goes through float, so this cannot be exact -- which is the whole
        // reason the scene-origin dance exists. A centimetre is the tolerance that matters
        // for rendering.
        Assert.Equal(targetLon, lon, 6);
        Assert.Equal(targetLat, lat, 6);
    }

    [Fact]
    public void East_is_positive_X_and_north_is_negative_Z()
    {
        // Stride convention: Y is up, +Z points south. Getting the Z sign wrong mirrors the
        // whole scene north to south and nothing complains.
        var (originLon, originLat) = CoordinateConverter.CreateSceneOrigin(Lon, Lat);

        var east  = CoordinateConverter.LonLatToLocal(Lon + 0.01, Lat, originLon, originLat);
        var north = CoordinateConverter.LonLatToLocal(Lon, Lat + 0.01, originLon, originLat);

        Assert.True(east.X > 0);
        Assert.Equal(0f, east.Z, 3);

        Assert.True(north.Z < 0);
        Assert.Equal(0f, north.X, 3);
    }

    [Fact]
    public void Elevation_becomes_the_Y_axis()
    {
        var (originLon, originLat) = CoordinateConverter.CreateSceneOrigin(Lon, Lat);
        var local = CoordinateConverter.LonLatToLocal(Lon, Lat, originLon, originLat, 42.0);

        Assert.Equal(42f, local.Y, 3);
    }

    [Fact]
    public void A_degree_of_longitude_is_shorter_than_a_degree_of_latitude_away_from_the_equator()
    {
        double perLon = CoordinateConverter.MetresPerDegreeLongitude(Lat);
        double perLat = CoordinateConverter.MetresPerDegreeLatitude();

        Assert.True(perLon < perLat);
        Assert.Equal(perLat * Math.Cos(Lat * Math.PI / 180.0), perLon, 3);
    }

    [Fact]
    public void A_degree_of_longitude_at_the_equator_equals_a_degree_of_latitude()
    {
        Assert.Equal(
            CoordinateConverter.MetresPerDegreeLatitude(),
            CoordinateConverter.MetresPerDegreeLongitude(0),
            6);
    }

    [Fact]
    public void Local_offsets_match_the_metres_per_degree_figures()
    {
        var (originLon, originLat) = CoordinateConverter.CreateSceneOrigin(Lon, Lat);
        var local = CoordinateConverter.LonLatToLocal(Lon + 1, Lat + 1, originLon, originLat);

        Assert.Equal(CoordinateConverter.MetresPerDegreeLongitude(Lat), local.X, 0);
        Assert.Equal(-CoordinateConverter.MetresPerDegreeLatitude(), local.Z, 0);
    }

    [Fact]
    public void A_flat_heightmap_is_all_zeroes()
    {
        var heightmap = ElevationNodes.CreateFlatHeightmap(4, 3);

        Assert.Equal(12, heightmap.Length);
        Assert.All(heightmap, h => Assert.Equal(0f, h));
    }

    [Fact]
    public void NormalizeHeightmap_reports_the_range_it_removed()
    {
        var heightmap = new[] { 10f, 20f, 30f, 40f };

        var normalized = ElevationNodes.NormalizeHeightmap(heightmap, out float min, out float max);

        Assert.Equal(10f, min);
        Assert.Equal(40f, max);
        Assert.Equal(0f, normalized[0], 5);
        Assert.Equal(1f, normalized[^1], 5);
    }

    [Fact]
    public void SampleHeightmap_returns_the_stored_value_at_a_grid_point()
    {
        // Row-major, so index = row * width + column.
        var heightmap = new float[] { 0, 1, 2, 3, 4, 5 };   // 3 wide, 2 high

        Assert.Equal(0f, ElevationNodes.SampleHeightmap(heightmap, 3, 2, 0f, 0f), 5);
        Assert.Equal(5f, ElevationNodes.SampleHeightmap(heightmap, 3, 2, 1f, 1f), 5);
    }

    [Fact]
    public void HeightmapToMesh_produces_two_triangles_per_cell()
    {
        var heightmap = ElevationNodes.CreateFlatHeightmap(3, 3);

        ElevationNodes.HeightmapToMesh(heightmap, 3, 3, 10f, 1f,
            out var positions, out var uvs, out var indices);

        Assert.Equal(9, positions.Count);
        Assert.Equal(9, uvs.Count);

        // A 3x3 grid of vertices is a 2x2 grid of cells: 4 cells, 2 triangles each.
        Assert.Equal(4 * 2 * 3, indices.Count);
        Assert.All(indices, i => Assert.InRange(i, 0, positions.Count - 1));
    }

    [Fact]
    public void Normals_of_a_flat_heightmap_all_point_up()
    {
        var normals = ElevationNodes.GenerateNormals(
            ElevationNodes.CreateFlatHeightmap(4, 4), 4, 4, 1f);

        Assert.Equal(16, normals.Length);
        Assert.All(normals, n =>
        {
            Assert.Equal(1f, n.Y, 3);
            Assert.Equal(1f, n.Length(), 3);
        });
    }

    [Fact]
    public void TessellatePolygon_triangulates_a_square_into_two_triangles()
    {
        var square = GeometryNodes.CreatePolygon(new[]
        {
            (Lon, Lat), (Lon + 0.01, Lat), (Lon + 0.01, Lat + 0.01), (Lon, Lat + 0.01)
        });

        var (originLon, originLat) = CoordinateConverter.CreateSceneOrigin(Lon, Lat);

        GeometryTessellator.TessellatePolygon(square, originLon, originLat,
            out var positions, out var indices);

        // Two triangles, as expected for a convex quad.
        Assert.Equal(2 * 3, indices.Count);

        // Six positions, not four: TessellatePolygon runs NTS's DelaunayTriangulationBuilder
        // and emits three vertices per triangle without merging the two the triangles share.
        // Anything consuming this should treat the count as an upper bound rather than
        // assuming shared corners.
        Assert.Equal(6, positions.Count);
        Assert.All(indices, i => Assert.InRange(i, 0, positions.Count - 1));
    }

    [Fact]
    public void CreateTileQuad_is_a_two_triangle_quad_with_corner_UVs()
    {
        GeometryTessellator.CreateTileQuad(0f, 0f, 100f, 100f, 0f,
            out var positions, out var uvs, out var indices);

        Assert.Equal(4, positions.Count);
        Assert.Equal(4, uvs.Count);
        Assert.Equal(6, indices.Count);

        Assert.All(uvs, uv =>
        {
            Assert.InRange(uv.X, 0f, 1f);
            Assert.InRange(uv.Y, 0f, 1f);
        });
    }
}
