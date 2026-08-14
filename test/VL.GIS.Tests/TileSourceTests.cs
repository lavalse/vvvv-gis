using VL.GIS.Tiles;

namespace VL.GIS.Tests;

/// <summary>
/// Cover for the code that replaced BruTile.
/// </summary>
/// <remarks>
/// Dropping BruTile is what lets VL.GIS and VL.Mapsui be installed at the same time — vvvv's
/// package folder holds one version of each library for everything it loads, and BruTile 6 and
/// the 5.x that Mapsui 4.1.9 pins are different assembly identities with an incompatible
/// <c>Attribution</c> layout.
///
/// What came back in-house is small — a URL template, a fetch, a directory of files — but it is
/// ours now, so it needs its own cover. The tile arithmetic was always ours and is tested in
/// <c>TilesTests</c>; nothing here touches the network.
/// </remarks>
public class TileSourceTests
{
    // ── The URL template ──────────────────────────────────────────────────────

    [Fact]
    public void The_template_substitutes_zoom_column_and_row()
    {
        var source = new HttpTileSource("https://example.com/{z}/{x}/{y}.png");

        Assert.Equal(
            "https://example.com/12/3637/1612.png",
            source.UrlFor(new TileIndex(3637, 1612, 12)));
    }

    [Fact]
    public void Column_and_row_do_not_get_swapped()
    {
        // {x} is the column and {y} the row, and a map made of the right tiles in the wrong
        // places looks like a network problem rather than a substitution bug.
        var source = new HttpTileSource("https://example.com/{z}/{x}/{y}.png");

        Assert.Equal("https://example.com/5/1/2.png", source.UrlFor(new TileIndex(1, 2, 5)));
    }

    [Fact]
    public void A_template_may_use_a_placeholder_more_than_once()
    {
        // Some services put the zoom in a path segment and again in a query string.
        var source = new HttpTileSource("https://example.com/{z}/{x}/{y}.png?z={z}");

        Assert.Equal(
            "https://example.com/7/1/2.png?z=7",
            source.UrlFor(new TileIndex(1, 2, 7)));
    }

    [Fact]
    public void The_real_OSM_url_comes_out_as_the_tile_server_expects()
    {
        // Tokyo at zoom 12, the same tile the rest of the suite uses.
        var url = TileFetchNodes.TileUrl(
            TileProviderNodes.OsmTileSource(), new TileIndex(3637, 1612, 12));

        Assert.Equal("https://tile.openstreetmap.org/12/3637/1612.png", url);
    }

    // ── Attribution ───────────────────────────────────────────────────────────

    [Fact]
    public void WithAttribution_keeps_the_template_intact()
    {
        // The bug this pins: rebuilding the source instead of wrapping it would have to recover
        // the template, and the only way to see one from outside is to ask for a tile's URL --
        // which has already substituted the placeholders. Every tile after the first would then
        // be the same tile, on a map that still looked plausible.
        var source = TileProviderNodes.WithAttribution(
            TileProviderNodes.XyzTileSource("https://example.com/{z}/{x}/{y}.png"),
            "© Someone");

        Assert.Equal("https://example.com/8/4/5.png",
            TileFetchNodes.TileUrl(source, new TileIndex(4, 5, 8)));
        Assert.Equal("© Someone", TileProviderNodes.TileAttribution(source));
    }

    [Fact]
    public void WithAttribution_carries_the_zoom_range_over()
    {
        var source = TileProviderNodes.WithAttribution(
            TileProviderNodes.XyzTileSource("https://example.com/{z}/{x}/{y}.png", "Mine", 3, 15),
            "© Someone", "https://example.com/terms");

        TileProviderNodes.TileSourceZoomRange(source, out int minZoom, out int maxZoom);

        Assert.Equal(3, minZoom);
        Assert.Equal(15, maxZoom);
        Assert.Equal("Mine", TileProviderNodes.TileSourceName(source));
        Assert.Equal("https://example.com/terms", TileProviderNodes.TileAttributionUrl(source));
    }

    [Fact]
    public void A_plain_XYZ_source_starts_with_no_attribution()
    {
        // Not an oversight: we cannot know what a stranger's tile server requires. The node
        // exists so the patch author states it.
        var source = TileProviderNodes.XyzTileSource("https://example.com/{z}/{x}/{y}.png");

        Assert.Equal(string.Empty, TileProviderNodes.TileAttribution(source));
    }

    // ── The file cache ────────────────────────────────────────────────────────

    [Fact]
    public void A_saved_tile_comes_back_byte_for_byte()
    {
        var dir = Path.Combine(Path.GetTempPath(), "VL.GIS.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cache = TileFetchNodes.CreateFileCache(dir);
            var index = new TileIndex(3637, 1612, 12);
            var bytes = new byte[] { 1, 2, 3, 4, 5 };

            cache.Save(index, bytes);

            Assert.Equal(bytes, TileFetchNodes.ReadCachedTile(cache, index));
            Assert.True(TileFetchNodes.IsTileCached(cache, index));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Tiles_do_not_collide_across_zoom_levels()
    {
        // {level}/{col}/{row}: flattening that to one directory would make 3637_1612 at zoom 12
        // and at zoom 13 the same file, so panning between zooms would serve the wrong picture.
        var dir = Path.Combine(Path.GetTempPath(), "VL.GIS.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cache = TileFetchNodes.CreateFileCache(dir);
            cache.Save(new TileIndex(3637, 1612, 12), new byte[] { 12 });
            cache.Save(new TileIndex(3637, 1612, 13), new byte[] { 13 });

            Assert.Equal(new byte[] { 12 }, cache.Find(new TileIndex(3637, 1612, 12)));
            Assert.Equal(new byte[] { 13 }, cache.Find(new TileIndex(3637, 1612, 13)));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void An_uncached_tile_reads_back_as_nothing_rather_than_throwing()
    {
        // The directory does not exist yet on the first frame of a patch, and a node that threw
        // there would take the whole document down.
        var cache = TileFetchNodes.CreateFileCache(
            Path.Combine(Path.GetTempPath(), "VL.GIS.Tests", "never-created"));

        Assert.False(TileFetchNodes.IsTileCached(cache, new TileIndex(0, 0, 0)));
        Assert.Null(TileFetchNodes.ReadCachedTile(cache, new TileIndex(0, 0, 0)));
    }

    [Fact]
    public void An_impossible_cache_directory_returns_null_rather_than_throwing()
    {
        // A path arrives from a pin, so it can be anything at all.
        var cache = TileFetchNodes.CreateFileCache(@"Z:\no\such\drive\tiles");

        Assert.Null(cache.Save(new TileIndex(0, 0, 0), new byte[] { 1 }));
        Assert.Null(cache.Find(new TileIndex(0, 0, 0)));
    }

    // ── The point of the whole exercise ───────────────────────────────────────

    [Fact]
    public void Nothing_in_the_tiles_assembly_references_BruTile()
    {
        // The assertion that VL.GIS and VL.Mapsui can be installed together. A stray using or a
        // returned BruTile type would put the dependency back and break the other package on
        // any machine that has both -- silently, because vvvv's package folder is flat and
        // whichever version lands there wins for everything.
        var referenced = typeof(TileIndex).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(referenced, a => a.Name?.Contains("BruTile") == true);
    }

    [Fact]
    public void Neither_does_the_Skia_assembly()
    {
        var referenced = typeof(VL.GIS.Skia.TileLayoutNodes).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(referenced, a => a.Name?.Contains("BruTile") == true);
    }
}
