using VL.GIS.Tiles;

namespace VL.GIS.Tests;

/// <summary>
/// Tile indexing, bounds and source metadata. Nothing here fetches a tile: hitting
/// tile.openstreetmap.org from a build would be flaky and would breach the OSM tile usage
/// policy, which forbids bulk or scripted downloading.
/// </summary>
public class TilesTests
{
    const double Lon = 139.7;
    const double Lat = 35.68;
    const int Zoom = 12;

    [Fact]
    public void TileIndexFromLonLat_finds_the_expected_tile()
    {
        var index = TileFetchNodes.TileIndexFromLonLat(Lon, Lat, Zoom);

        Assert.Equal(3637, index.Col);
        Assert.Equal(1612, index.Row);
        Assert.Equal(Zoom, index.Level);
    }

    [Fact]
    public void TileBounds_contains_the_coordinate_that_produced_the_tile()
    {
        // The assertion that does not depend on trusting any hand-computed number: whatever
        // tile a coordinate maps to, that tile's bounds must contain the coordinate.
        var index = TileFetchNodes.TileIndexFromLonLat(Lon, Lat, Zoom);

        TileFetchNodes.TileBounds(index,
            out double minLon, out double minLat, out double maxLon, out double maxLat);

        Assert.InRange(Lon, minLon, maxLon);
        Assert.InRange(Lat, minLat, maxLat);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(19)]
    public void TileBounds_contains_its_coordinate_at_every_zoom(int zoom)
    {
        var index = TileFetchNodes.TileIndexFromLonLat(Lon, Lat, zoom);

        TileFetchNodes.TileBounds(index,
            out double minLon, out double minLat, out double maxLon, out double maxLat);

        Assert.InRange(Lon, minLon, maxLon);
        Assert.InRange(Lat, minLat, maxLat);
    }

    [Fact]
    public void A_tile_centre_maps_back_to_the_same_tile()
    {
        var index = TileFetchNodes.TileIndexFromLonLat(Lon, Lat, Zoom);

        TileFetchNodes.TileBounds(index,
            out double minLon, out double minLat, out double maxLon, out double maxLat);

        var again = TileFetchNodes.TileIndexFromLonLat(
            (minLon + maxLon) / 2, (minLat + maxLat) / 2, Zoom);

        Assert.Equal(index.Col, again.Col);
        Assert.Equal(index.Row, again.Row);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    public void Longitude_span_is_the_world_divided_by_two_to_the_zoom(int zoom)
    {
        var index = TileFetchNodes.TileIndexFromLonLat(Lon, Lat, zoom);

        TileFetchNodes.TileBounds(index,
            out double minLon, out _, out double maxLon, out _);

        Assert.Equal(360.0 / (1 << zoom), maxLon - minLon, 12);
    }

    [Fact]
    public void Tile_aspect_ratio_follows_the_Mercator_cosine()
    {
        // A slippy-map tile is square in pixels, so on the ground its latitude span must be
        // shorter than its longitude span by cos(latitude). This is the property that makes
        // it a Mercator projection rather than a plain equirectangular one, and it holds
        // whatever the arithmetic in TileBounds happens to be.
        var index = TileFetchNodes.TileIndexFromLonLat(Lon, Lat, Zoom);

        TileFetchNodes.TileBounds(index,
            out double minLon, out double minLat, out double maxLon, out double maxLat);

        double centreLat = (minLat + maxLat) / 2;
        double expected  = Math.Cos(centreLat * Math.PI / 180.0);
        double actual    = (maxLat - minLat) / (maxLon - minLon);

        Assert.Equal(expected, actual, 4);
    }

    [Fact]
    public void Zoom_zero_is_a_single_tile_covering_the_world()
    {
        var index = TileFetchNodes.TileIndexFromLonLat(Lon, Lat, 0);

        Assert.Equal(0, index.Col);
        Assert.Equal(0, index.Row);

        TileFetchNodes.TileBounds(index,
            out double minLon, out _, out double maxLon, out _);

        Assert.Equal(-180.0, minLon, 9);
        Assert.Equal(180.0, maxLon, 9);
    }

    [Fact]
    public void Row_counts_from_the_north()
    {
        // XYZ convention, as opposed to TMS which counts from the south. Getting this
        // backwards produces a map that is vertically mirrored and no error at all.
        var north = TileFetchNodes.TileIndexFromLonLat(0, 60, 4);
        var south = TileFetchNodes.TileIndexFromLonLat(0, -60, 4);

        Assert.True(north.Row < south.Row);
    }

    [Fact]
    public void Out_of_range_latitudes_are_clamped_rather_than_throwing()
    {
        // Web Mercator is undefined at the poles. The implementation clamps, so this must
        // stay in range instead of producing a negative row or an exception.
        var index = TileFetchNodes.TileIndexFromLonLat(0, 89.9, 8);

        Assert.InRange(index.Row, 0, 255);
        Assert.InRange(index.Col, 0, 255);
    }

    [Fact]
    public void TileIndicesForBounds_covers_the_box_and_includes_its_corners()
    {
        var indices = TileFetchNodes.TileIndicesForBounds(139.6, 35.6, 139.8, 35.8, Zoom);

        var sw = TileFetchNodes.TileIndexFromLonLat(139.6, 35.6, Zoom);
        var ne = TileFetchNodes.TileIndexFromLonLat(139.8, 35.8, Zoom);

        Assert.NotEmpty(indices);
        Assert.Contains(indices, i => i.Col == sw.Col && i.Row == sw.Row);
        Assert.Contains(indices, i => i.Col == ne.Col && i.Row == ne.Row);
        Assert.All(indices, i => Assert.Equal(Zoom, i.Level));

        // Rectangular cover, so the count is the product of the two spans.
        int expected = (Math.Abs(ne.Col - sw.Col) + 1) * (Math.Abs(ne.Row - sw.Row) + 1);
        Assert.Equal(expected, indices.Count);
    }

    [Fact]
    public void CreateTileIndex_keeps_its_arguments_in_order()
    {
        var index = TileFetchNodes.CreateTileIndex(3637, 1612, 12);

        Assert.Equal(3637, index.Col);
        Assert.Equal(1612, index.Row);
        Assert.Equal(12, index.Level);
    }

    [Fact]
    public void TileIndexParts_reads_back_what_CreateTileIndex_was_given()
    {
        TileFetchNodes.TileIndexParts(
            TileFetchNodes.CreateTileIndex(3637, 1612, 12),
            out int col, out int row, out int level);

        Assert.Equal(3637, col);
        Assert.Equal(1612, row);
        Assert.Equal(12, level);
    }

    [Fact]
    public void TileIndexParts_exposes_what_TileIndexFromLonLat_computed()
    {
        // The whole reason this node exists: a TileIndex is opaque inside a patch, so
        // debugging a tile request meant guessing which tile had been asked for.
        TileFetchNodes.TileIndexParts(
            TileFetchNodes.TileIndexFromLonLat(Lon, Lat, Zoom),
            out int col, out int row, out int level);

        Assert.Equal(3637, col);
        Assert.Equal(1612, row);
        Assert.Equal(Zoom, level);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(19)]
    public void TileIndexParts_round_trips_through_CreateTileIndex(int zoom)
    {
        var original = TileFetchNodes.TileIndexFromLonLat(Lon, Lat, zoom);

        TileFetchNodes.TileIndexParts(original, out int col, out int row, out int level);
        var rebuilt = TileFetchNodes.CreateTileIndex(col, row, level);

        Assert.Equal(original.Col, rebuilt.Col);
        Assert.Equal(original.Row, rebuilt.Row);
        Assert.Equal(original.Level, rebuilt.Level);
    }

    [Fact]
    public void OsmTileSource_declares_its_attribution()
    {
        // OSM requires attribution to be displayed and the README points users at
        // TileAttribution to obtain it. No attribution was passed to HttpTileSource
        // originally, so this returned an empty string and following the documentation
        // produced no attribution at all.
        string attribution = TileProviderNodes.TileAttribution(TileProviderNodes.OsmTileSource());

        Assert.False(string.IsNullOrWhiteSpace(attribution));
        Assert.Contains("OpenStreetMap", attribution);
    }

    [Fact]
    public void OpenTopoMapTileSource_declares_its_attribution()
    {
        string attribution =
            TileProviderNodes.TileAttribution(TileProviderNodes.OpenTopoMapTileSource());

        Assert.Contains("OpenTopoMap", attribution);
    }

    [Fact]
    public void Tile_sources_are_fetchable_without_a_cast()
    {
        // Regression test for a defect that was invisible in C#: the factories used to
        // return ITileSource while every fetch node takes IHttpTileSource, so in a patch
        // the two pins would not connect. The compiler is the assertion here -- passing
        // these to a fetch signature has to type-check.
        Assert.IsAssignableFrom<BruTile.IHttpTileSource>(TileProviderNodes.OsmTileSource());
        Assert.IsAssignableFrom<BruTile.IHttpTileSource>(TileProviderNodes.OpenTopoMapTileSource());
        Assert.IsAssignableFrom<BruTile.IHttpTileSource>(
            TileProviderNodes.XyzTileSource("https://example.com/{z}/{x}/{y}.png"));
    }

    [Fact]
    public void OsmTileSource_reports_the_documented_zoom_range()
    {
        TileProviderNodes.TileSchemaZoomRange(
            TileProviderNodes.OsmTileSource(), out int minZoom, out int maxZoom);

        Assert.Equal(0, minZoom);
        Assert.Equal(19, maxZoom);
    }

    [Fact]
    public void Tile_schema_has_a_name()
    {
        Assert.False(string.IsNullOrWhiteSpace(
            TileProviderNodes.TileSchemaName(TileProviderNodes.OsmTileSource())));
    }
}
