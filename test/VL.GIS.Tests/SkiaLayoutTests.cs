using SkiaSharp;
using VL.GIS.Skia;
using VL.GIS.Tiles;

namespace VL.GIS.Tests;

/// <summary>
/// Tile placement and geometry-to-path conversion. Nothing here draws or fetches; these are
/// about where things end up.
/// </summary>
public class SkiaLayoutTests
{
    const double Lon = 139.7;
    const double Lat = 35.68;

    static MapView Tokyo(double zoom = 12, float w = 512, float h = 512)
        => ViewportNodes.CreateMapView(Lon, Lat, zoom, w, h);

    [Fact]
    public void A_512_px_view_needs_between_four_and_nine_tiles()
    {
        // 512 px covers exactly two 256 px tiles per axis when aligned, three when not. Any
        // other count means the visible rectangle is being computed wrongly.
        var tiles = TileLayoutNodes.VisibleTiles(Tokyo());

        Assert.InRange(tiles.Count, 4, 9);
        Assert.All(tiles, t => Assert.Equal(12, t.Level));
    }

    [Fact]
    public void The_visible_tiles_include_the_one_under_the_centre()
    {
        var expected = TileFetchNodes.TileIndexFromLonLat(Lon, Lat, 12);
        var tiles = TileLayoutNodes.VisibleTiles(Tokyo());

        Assert.Contains(tiles, t => t.Col == expected.Col && t.Row == expected.Row);
    }

    [Fact]
    public void Every_visible_tile_overlaps_the_view()
    {
        // A tile that is fetched but lands entirely off screen is wasted bandwidth, and at
        // OSM's rate limits that matters.
        var view = Tokyo();
        var viewRect = new SKRect(0, 0, view.Width, view.Height);

        foreach (var tile in TileLayoutNodes.VisibleTiles(view))
        {
            var dest = TileLayoutNodes.TileDestination(view, tile);
            Assert.True(dest.IntersectsWith(viewRect), $"tile {tile.Col}/{tile.Row} is off screen at {dest}");
        }
    }

    [Fact]
    public void The_visible_tiles_cover_the_whole_view()
    {
        // The other half of the previous test: no gaps. Sample a grid of pixels and check
        // each is inside some tile's rectangle.
        var view = Tokyo();
        var rects = new List<SKRect>();
        foreach (var tile in TileLayoutNodes.VisibleTiles(view))
            rects.Add(TileLayoutNodes.TileDestination(view, tile));

        for (float x = 1; x < view.Width; x += 64)
            for (float y = 1; y < view.Height; y += 64)
                Assert.True(rects.Exists(r => r.Contains(x, y)), $"({x}, {y}) is not covered");
    }

    [Fact]
    public void Tiles_are_256_pixels_at_their_own_zoom()
    {
        var dest = TileLayoutNodes.TileDestination(
            Tokyo(12), TileFetchNodes.TileIndexFromLonLat(Lon, Lat, 12));

        Assert.Equal(256f, dest.Width, 3);
        Assert.Equal(256f, dest.Height, 3);
    }

    [Fact]
    public void Adjacent_tiles_abut_exactly()
    {
        // A sub-pixel gap between tiles shows up as seams across the whole map.
        var view = Tokyo();
        var left  = TileLayoutNodes.TileDestination(view, TileFetchNodes.CreateTileIndex(3637, 1612, 12));
        var right = TileLayoutNodes.TileDestination(view, TileFetchNodes.CreateTileIndex(3638, 1612, 12));
        var below = TileLayoutNodes.TileDestination(view, TileFetchNodes.CreateTileIndex(3637, 1613, 12));

        Assert.Equal(left.Right, right.Left, 4);
        Assert.Equal(left.Bottom, below.Top, 4);
    }

    [Fact]
    public void VisibleTileLayout_pairs_each_tile_with_its_own_rectangle()
    {
        TileLayoutNodes.VisibleTileLayout(Tokyo(), out var tiles, out var rects);

        Assert.Equal(tiles.Count, rects.Count);
        for (int i = 0; i < tiles.Count; i++)
            Assert.Equal(TileLayoutNodes.TileDestination(Tokyo(), tiles[i]), rects[i]);
    }

    [Fact]
    public void Zoom_zero_asks_for_exactly_one_tile()
    {
        var tiles = TileLayoutNodes.VisibleTiles(ViewportNodes.CreateMapView(0, 0, 0, 256, 256));

        Assert.Single(tiles);
        Assert.Equal(0, tiles[0].Col);
        Assert.Equal(0, tiles[0].Row);
    }

    [Fact]
    public void A_view_wider_than_the_world_does_not_ask_for_tiles_that_do_not_exist()
    {
        // At zoom 0 the world is 256 px, so a 1024 px view overshoots it in every direction.
        var tiles = TileLayoutNodes.VisibleTiles(ViewportNodes.CreateMapView(0, 0, 0, 1024, 1024));

        Assert.All(tiles, t =>
        {
            Assert.InRange(t.Col, 0, 0);
            Assert.InRange(t.Row, 0, 0);
        });
    }

    [Fact]
    public void DecodeTile_returns_null_instead_of_throwing_on_bad_input()
    {
        Assert.Null(TileLayoutNodes.DecodeTile(null));
        Assert.Null(TileLayoutNodes.DecodeTile(Array.Empty<byte>()));
        Assert.Null(TileLayoutNodes.DecodeTile(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void DecodeTile_reads_a_real_PNG()
    {
        // Built here rather than downloaded, so this stays a no-network test.
        using var surface = SKSurface.Create(new SKImageInfo(8, 8));
        surface.Canvas.Clear(SKColors.Red);
        using var encoded = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);

        var image = TileLayoutNodes.DecodeTile(encoded.ToArray());

        Assert.NotNull(image);
        Assert.Equal(8, image!.Width);
        Assert.Equal(8, image.Height);
    }

    // ── geometry to path ──────────────────────────────────────────────────────

    [Fact]
    public void A_point_becomes_a_circle_at_its_screen_position()
    {
        var view = Tokyo();
        var path = GeometryPathNodes.GeometryToPath(view, GeometryNodes.CreatePoint(Lon, Lat), 4f);

        Assert.False(path.IsEmpty);

        // The circle is centred on the point, which is the centre of the view.
        Assert.Equal(256f, path.Bounds.MidX, 2);
        Assert.Equal(256f, path.Bounds.MidY, 2);
        Assert.Equal(8f, path.Bounds.Width, 2);
    }

    [Fact]
    public void A_polygon_becomes_a_closed_path_covering_its_extent()
    {
        var view = Tokyo();
        var square = GeometryNodes.CreatePolygon(new[]
        {
            (Lon - 0.05, Lat - 0.05), (Lon + 0.05, Lat - 0.05),
            (Lon + 0.05, Lat + 0.05), (Lon - 0.05, Lat + 0.05)
        });

        var path = GeometryPathNodes.GeometryToPath(view, square);

        Assert.False(path.IsEmpty);

        // Longitude is linear in Mercator, so the horizontal centre lands exactly.
        Assert.Equal(256f, path.Bounds.MidX, 1);

        // Latitude is not. Mercator stretches more the further from the equator, so the
        // northern half of a latitude-symmetric shape occupies more pixels than the southern
        // half, and the shape's pixel centre sits slightly north of the view centre. Screen y
        // grows downward, hence "less than". Expecting exactly 256 here is the mistake, not
        // the number.
        Assert.True(path.Bounds.MidY < 256f);
        Assert.Equal(256f, path.Bounds.MidY, 0);
    }

    [Fact]
    public void A_polygon_with_a_hole_produces_both_contours()
    {
        var view = Tokyo();
        var shell = new[] { (Lon - 0.1, Lat - 0.1), (Lon + 0.1, Lat - 0.1), (Lon + 0.1, Lat + 0.1), (Lon - 0.1, Lat + 0.1) };
        var hole  = new[] { (Lon - 0.02, Lat - 0.02), (Lon + 0.02, Lat - 0.02), (Lon + 0.02, Lat + 0.02), (Lon - 0.02, Lat + 0.02) };

        var solid = GeometryPathNodes.GeometryToPath(view, GeometryNodes.CreatePolygon(shell));
        var holed = GeometryPathNodes.GeometryToPath(
            view, GeometryNodes.CreatePolygonWithHoles(shell, new[] { hole }));

        // Same outline, more contours: the hole is a second closed subpath inside the first.
        Assert.Equal(solid.Bounds, holed.Bounds);
        Assert.True(holed.PointCount > solid.PointCount);

        // The hole is genuinely empty, which only holds because the two rings are given
        // opposite winding. Without that the non-zero fill rule treats the inner ring as more
        // solid rather than as a hole, and this is the assertion that caught it.
        Assert.True(holed.Contains(256f + 60f, 256f));   // inside the shell, outside the hole
        Assert.False(holed.Contains(256f, 256f));        // dead centre, inside the hole

        // A polygon without holes stays solid throughout.
        Assert.True(solid.Contains(256f, 256f));
    }

    [Fact]
    public void A_line_follows_its_coordinates_in_order()
    {
        var view = Tokyo();
        var line = GeometryNodes.CreateLineString(new[]
        {
            (Lon - 0.05, Lat), (Lon, Lat), (Lon + 0.05, Lat)
        });

        var path = GeometryPathNodes.GeometryToPath(view, line);

        Assert.Equal(3, path.PointCount);
        var (startX, _) = ViewportNodes.LonLatToScreen(view, Lon - 0.05, Lat);
        Assert.Equal(startX, path[0].X, 2);
    }

    [Fact]
    public void An_empty_or_missing_geometry_gives_an_empty_path_rather_than_null()
    {
        Assert.True(GeometryPathNodes.GeometryToPath(Tokyo(), null).IsEmpty);
        Assert.True(GeometryPathNodes.GeometriesToPath(Tokyo(), null).IsEmpty);
    }

    [Fact]
    public void GeometriesToPath_merges_several_geometries_into_one()
    {
        var view = Tokyo();
        var a = GeometryNodes.CreatePoint(Lon - 0.05, Lat);
        var b = GeometryNodes.CreatePoint(Lon + 0.05, Lat);

        var merged = GeometryPathNodes.GeometriesToPath(view, new[] { (NetTopologySuite.Geometries.Geometry)a, b });
        var single = GeometryPathNodes.GeometryToPath(view, a);

        Assert.True(merged.Bounds.Width > single.Bounds.Width);
        Assert.Equal(256f, merged.Bounds.MidX, 1);
    }
}
