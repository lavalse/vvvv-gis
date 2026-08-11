using VL.GIS.Skia;
using VL.GIS.Tiles;

namespace VL.GIS.Tests;

/// <summary>
/// The viewport maths that everything in GIS.Skia rests on. No rendering here: these check
/// where things land, which is what goes wrong silently.
/// </summary>
public class SkiaViewportTests
{
    const double Lon = 139.7;
    const double Lat = 35.68;

    static MapView Tokyo(double zoom = 12, float w = 512, float h = 512)
        => ViewportNodes.CreateMapView(Lon, Lat, zoom, w, h);

    [Fact]
    public void The_centre_of_the_view_is_the_centre_of_the_screen()
    {
        var (x, y) = ViewportNodes.LonLatToScreen(Tokyo(), Lon, Lat);

        Assert.Equal(256f, x, 3);
        Assert.Equal(256f, y, 3);
    }

    [Fact]
    public void Screen_and_geographic_coordinates_round_trip()
    {
        var view = Tokyo();

        foreach (var (sx, sy) in new[] { (0f, 0f), (511f, 300f), (256f, 256f), (100f, 480f) })
        {
            var (lon, lat) = ViewportNodes.ScreenToLonLat(view, sx, sy);
            var (bx, by) = ViewportNodes.LonLatToScreen(view, lon, lat);

            Assert.Equal(sx, bx, 2);
            Assert.Equal(sy, by, 2);
        }
    }

    [Fact]
    public void East_is_right_and_north_is_up()
    {
        // Screen y grows downward while latitude grows upward. Getting this backwards
        // mirrors the map vertically and nothing complains, which is the same trap as the
        // XYZ row convention in GIS.Tiles.
        var view = Tokyo();
        var (centreX, centreY) = ViewportNodes.LonLatToScreen(view, Lon, Lat);

        var (eastX, _) = ViewportNodes.LonLatToScreen(view, Lon + 0.05, Lat);
        var (_, northY) = ViewportNodes.LonLatToScreen(view, Lon, Lat + 0.05);

        Assert.True(eastX > centreX);
        Assert.True(northY < centreY);
    }

    [Fact]
    public void Zooming_in_one_level_doubles_the_pixel_distance()
    {
        var near = ViewportNodes.LonLatToScreen(Tokyo(12), Lon + 0.1, Lat);
        var far  = ViewportNodes.LonLatToScreen(Tokyo(13), Lon + 0.1, Lat);

        // The centre is at 256, so the offsets from it are what scale.
        Assert.Equal(2.0, (far.x - 256f) / (near.x - 256f), 3);
    }

    [Fact]
    public void The_whole_world_spans_one_tile_at_zoom_zero()
    {
        var world = ViewportNodes.CreateMapView(0, 0, 0, 256, 256);

        var (westX, _) = ViewportNodes.LonLatToScreen(world, -180, 0);
        var (eastX, _) = ViewportNodes.LonLatToScreen(world, 180, 0);

        Assert.Equal(0f, westX, 3);
        Assert.Equal(256f, eastX, 3);
    }

    [Fact]
    public void ViewBounds_contains_the_centre_and_matches_the_corners()
    {
        var view = Tokyo();
        ViewportNodes.ViewBounds(view,
            out double minLon, out double minLat, out double maxLon, out double maxLat);

        Assert.InRange(Lon, minLon, maxLon);
        Assert.InRange(Lat, minLat, maxLat);

        // The bounds are exactly what the corner pixels decode to.
        var (westLon, northLat) = ViewportNodes.ScreenToLonLat(view, 0f, 0f);
        Assert.Equal(westLon, minLon, 9);
        Assert.Equal(northLat, maxLat, 9);
    }

    [Fact]
    public void Panning_moves_the_map_the_way_dragging_does()
    {
        var view = Tokyo();
        var (before, _) = ViewportNodes.LonLatToScreen(view, Lon, Lat);

        // Dragging the map 100 px to the right should carry the centre point with it.
        var panned = ViewportNodes.PanByPixels(view, 100f, 0f);
        var (after, _) = ViewportNodes.LonLatToScreen(panned, Lon, Lat);

        Assert.Equal(before + 100f, after, 2);
    }

    [Fact]
    public void Panning_and_panning_back_returns_to_the_same_view()
    {
        var view = Tokyo();
        var there = ViewportNodes.PanByPixels(view, 137f, -64f);
        var back  = ViewportNodes.PanByPixels(there, -137f, 64f);

        Assert.Equal(view.CenterLongitude, back.CenterLongitude, 9);
        Assert.Equal(view.CenterLatitude, back.CenterLatitude, 9);
    }

    [Fact]
    public void Zooming_around_a_pixel_holds_that_pixel_still()
    {
        // What a scroll wheel over a map must do: the place under the cursor stays under the
        // cursor. Easy to get subtly wrong, and very visible once it is.
        var view = Tokyo();
        var (anchorLon, anchorLat) = ViewportNodes.ScreenToLonLat(view, 100f, 400f);

        var zoomed = ViewportNodes.ZoomAround(view, 100f, 400f, 1.0);
        var (x, y) = ViewportNodes.LonLatToScreen(zoomed, anchorLon, anchorLat);

        Assert.Equal(100f, x, 1);
        Assert.Equal(400f, y, 1);
        Assert.Equal(13.0, zoomed.Zoom, 9);
    }

    [Fact]
    public void Resolution_shrinks_by_half_per_zoom_level()
    {
        Assert.Equal(2.0, ViewportNodes.Resolution(Tokyo(12)) / ViewportNodes.Resolution(Tokyo(13)), 6);
    }

    [Fact]
    public void Resolution_at_the_equator_matches_the_known_figure()
    {
        // Zoom 0, 256 px for 40075 km of equator: about 156.5 km per pixel. This is the
        // number every slippy-map resolution table starts from.
        var equator = ViewportNodes.CreateMapView(0, 0, 0, 256, 256);

        Assert.InRange(ViewportNodes.Resolution(equator), 156_400, 156_600);
    }

    [Fact]
    public void MapViewInfo_reads_back_what_CreateMapView_was_given()
    {
        ViewportNodes.MapViewInfo(Tokyo(13, 800, 600),
            out double lon, out double lat, out double zoom, out float w, out float h);

        Assert.Equal(Lon, lon, 9);
        Assert.Equal(Lat, lat, 9);
        Assert.Equal(13.0, zoom, 9);
        Assert.Equal(800f, w);
        Assert.Equal(600f, h);
    }

    [Fact]
    public void Latitude_is_clamped_at_the_Mercator_limit_rather_than_running_to_infinity()
    {
        // Web Mercator sends the poles to infinity. Clamping keeps a pole-ward view finite
        // instead of producing NaN coordinates that would silently poison everything after.
        var view = ViewportNodes.CreateMapView(0, 0, 2, 512, 512);
        var (_, y) = ViewportNodes.LonLatToScreen(view, 0, 89.9999);

        Assert.False(float.IsNaN(y));
        Assert.False(float.IsInfinity(y));
    }

    [Fact]
    public void The_screen_position_agrees_with_the_tile_the_coordinate_falls_in()
    {
        // Ties GIS.Skia to GIS.Tiles: the pixel a coordinate lands on must sit inside the
        // rectangle drawn for the tile that GIS.Tiles says contains it. If these two
        // disagree, vectors drift away from the basemap under them.
        var view = Tokyo(12);
        var tile = TileFetchNodes.TileIndexFromLonLat(Lon, Lat, 12);

        var rect = TileLayoutNodes.TileDestination(view, tile);
        var (x, y) = ViewportNodes.LonLatToScreen(view, Lon, Lat);

        Assert.InRange(x, rect.Left, rect.Right);
        Assert.InRange(y, rect.Top, rect.Bottom);
    }
}
