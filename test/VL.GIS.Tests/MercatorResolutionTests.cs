using VL.GIS.Skia;

namespace VL.GIS.Tests;

/// <summary>
/// The bridge between a map engine's resolution and a MapView's zoom.
/// </summary>
/// <remarks>
/// VL.Mapsui states where its map is looking as metres per pixel; VL.GIS.Skia builds a view from
/// a zoom level. These convert, which is what lets one package draw data over the other's map
/// without either depending on the other.
///
/// The whole reason for a separate node is the trap pinned below: <c>Resolution</c> and
/// <c>MercatorResolution</c> are different quantities that agree only at the equator.
/// </remarks>
public class MercatorResolutionTests
{
    const double Tokyo = 35.68;
    const double Berlin = 52.52;

    static MapView View(double latitude, double zoom)
        => ViewportNodes.CreateMapView(139.7, latitude, zoom, 800, 600);

    [Fact]
    public void Zoom_zero_is_the_resolution_every_tile_service_quotes()
    {
        // 156543.034 m/px is the number in every slippy-map reference, and Mapsui's own zoom
        // ladder is 156543.033928 / 2^level. Agreeing with it to six decimals is what makes the
        // two engines line up.
        Assert.Equal(156543.033928, ViewportNodes.MercatorResolution(View(0, 0)), 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(19)]
    public void Each_zoom_level_halves_the_resolution(int zoom)
    {
        Assert.Equal(156543.033928 / Math.Pow(2, zoom),
            ViewportNodes.MercatorResolution(View(Tokyo, zoom)), 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3.5)]
    [InlineData(12)]
    [InlineData(19)]
    public void Zoom_and_resolution_round_trip(double zoom)
    {
        var resolution = ViewportNodes.MercatorResolution(View(Tokyo, zoom));

        Assert.Equal(zoom, ViewportNodes.ZoomFromMercatorResolution(resolution), 9);
    }

    [Fact]
    public void The_mercator_resolution_does_not_change_with_latitude()
    {
        // The defining property. A projection metre is a projection metre wherever you stand,
        // which is exactly why a map engine states its resolution this way.
        Assert.Equal(
            ViewportNodes.MercatorResolution(View(0, 12)),
            ViewportNodes.MercatorResolution(View(Berlin, 12)), 9);
    }

    [Fact]
    public void Ground_resolution_and_mercator_resolution_differ_by_the_cosine_of_the_latitude()
    {
        // The trap, stated as an equation. Resolution reports ground metres at the centre
        // latitude; MercatorResolution reports projection metres. Web Mercator stretches by
        // 1/cos(latitude), so passing one where the other belongs scales an overlay by cos(lat)
        // -- and the map still looks like a map, merely a little out of register.
        foreach (var latitude in new[] { 0.0, Tokyo, Berlin, 70.0 })
        {
            var view = View(latitude, 12);
            var expected = Math.Cos(latitude * Math.PI / 180.0);

            Assert.Equal(expected,
                ViewportNodes.Resolution(view) / ViewportNodes.MercatorResolution(view), 9);
        }
    }

    [Fact]
    public void Using_the_wrong_one_at_Tokyo_is_wrong_by_a_fifth()
    {
        // The size of the mistake, so the comment in ViewportNodes is a measurement rather than
        // an adjective. If someone "simplifies" the two nodes into one, this is what breaks.
        var view = View(Tokyo, 12);

        var right = ViewportNodes.ZoomFromMercatorResolution(ViewportNodes.MercatorResolution(view));
        var wrong = ViewportNodes.ZoomFromMercatorResolution(ViewportNodes.Resolution(view));

        Assert.Equal(12, right, 9);
        Assert.True(Math.Abs(right - wrong) > 0.29,
            $"Expected the wrong resolution to shift the zoom by about 0.3 levels, got {right - wrong}");
    }

    [Fact]
    public void A_resolution_of_zero_does_not_produce_infinity()
    {
        // A map engine reports resolution 0 before its viewport has a size, which is the state a
        // patch is in on its first frame. Infinity there would poison every downstream number.
        Assert.Equal(0, ViewportNodes.ZoomFromMercatorResolution(0));
        Assert.Equal(0, ViewportNodes.ZoomFromMercatorResolution(-1));
    }
}
