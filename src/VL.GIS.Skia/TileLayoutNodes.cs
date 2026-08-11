using System;
using System.Collections.Generic;
using BruTile;
using SkiaSharp;
using VL.Core.Import;

namespace VL.GIS.Skia;

/// <summary>
/// Works out which map tiles a view needs and where each one belongs on screen.
/// </summary>
/// <remarks>
/// Positions are pixels from the top-left of the view. The chain is: fetch each index with
/// GIS.Tiles, decode the bytes with DecodeTile, take the rectangle from TileDestinationParts,
/// **put it through GIS.Skia.Viewport ToRendererSpace**, and draw it with DrawImage.
///
/// That conversion is not optional. VL.Skia does not draw in pixels — its default space is
/// roughly 2.8 by 2 units — so a pixel position of a few hundred lands far off screen with
/// nothing drawn and no error. Two DrawImage pins are equally silent when wrong: Size Mode
/// must be <c>Size</c> and Anchor <c>TopLeft</c>. See help/HowTo Show a map.vl.
/// </remarks>
[Name("Tiles")]
public static class TileLayoutNodes
{
    /// <summary>
    /// Every tile index needed to fill the view, at the given zoom level.
    /// Pass -1 for zoomLevel to use the view's own zoom rounded down, which draws tiles at
    /// their natural size.
    /// VL.GIS's own implementation; TileIndex is BruTile's type.
    /// </summary>
    public static IReadOnlyList<TileIndex> VisibleTiles(MapView view, int zoomLevel = -1)
    {
        int z = zoomLevel < 0 ? (int)Math.Floor(view.Zoom) : zoomLevel;
        z = Math.Max(0, Math.Min(22, z));

        double worldSize = MapView.TileSize * Math.Pow(2.0, z);
        int tilesPerAxis = 1 << z;

        var (cx, cy) = ViewportNodes.LonLatToWorld(
            view.CenterLongitude, view.CenterLatitude, worldSize);

        // Convert the view rectangle into world pixels at the tile zoom, then to tile
        // numbers. Going through world pixels rather than through lon/lat corners keeps this
        // correct when the view is wider than the world.
        double left   = cx - view.Width / 2.0;
        double top    = cy - view.Height / 2.0;
        double right  = cx + view.Width / 2.0;
        double bottom = cy + view.Height / 2.0;

        int minCol = (int)Math.Floor(left / MapView.TileSize);
        int maxCol = (int)Math.Floor((right - 1e-9) / MapView.TileSize);
        int minRow = (int)Math.Floor(top / MapView.TileSize);
        int maxRow = (int)Math.Floor((bottom - 1e-9) / MapView.TileSize);

        // Rows do not wrap: there is no world above the north edge. Columns are clamped too
        // rather than wrapped, because a wrapped column would need a second destination
        // rectangle for the same tile and the caller has no way to express that yet.
        minRow = Math.Max(0, minRow);
        maxRow = Math.Min(tilesPerAxis - 1, maxRow);
        minCol = Math.Max(0, minCol);
        maxCol = Math.Min(tilesPerAxis - 1, maxCol);

        var result = new List<TileIndex>();
        for (int col = minCol; col <= maxCol; col++)
            for (int row = minRow; row <= maxRow; row++)
                result.Add(new TileIndex(col, row, z));
        return result;
    }

    /// <summary>
    /// Where a tile belongs on screen, as a rectangle in pixels from the top-left of the view.
    /// Prefer TileDestinationParts for drawing: VL.Skia's public image node takes a position
    /// and a size rather than a rectangle.
    /// VL.GIS's own implementation; TileIndex is BruTile's type, SKRect is SkiaSharp's.
    /// </summary>
    public static SKRect TileDestination(MapView view, TileIndex tileIndex)
    {
        double worldSize = MapView.TileSize * Math.Pow(2.0, tileIndex.Level);
        var (cx, cy) = ViewportNodes.LonLatToWorld(
            view.CenterLongitude, view.CenterLatitude, worldSize);

        double left = tileIndex.Col * MapView.TileSize - cx + view.Width / 2.0;
        double top  = tileIndex.Row * MapView.TileSize - cy + view.Height / 2.0;

        return new SKRect(
            (float)left,
            (float)top,
            (float)(left + MapView.TileSize),
            (float)(top + MapView.TileSize));
    }

    /// <summary>
    /// Where a tile belongs on screen, as a position and a size rather than a rectangle.
    /// These are pixels: pass them through GIS.Skia.Viewport ToRendererSpace before building
    /// the Vector2s for DrawImage, and set its Size Mode to <c>Size</c> and Anchor to
    /// <c>TopLeft</c>.
    /// VL.GIS's own implementation; TileIndex is BruTile's type.
    /// </summary>
    public static void TileDestinationParts(
        MapView view, TileIndex tileIndex,
        out float x, out float y, out float width, out float height)
    {
        // DrawImage is the public layer node; ImageLayer, which takes an SKRect directly, is
        // internal to VL.Skia and vvvvc rejects it with "Not found: ImageLayer". An SKRect is
        // also opaque in a patch -- there is no node to open one -- so a caller given only
        // TileDestination could see neither the numbers nor a way to use them. Plain floats
        // connect to anything and can be read.
        var rect = TileDestination(view, tileIndex);
        x      = rect.Left;
        y      = rect.Top;
        width  = rect.Width;
        height = rect.Height;
    }

    /// <summary>
    /// Both halves at once: the tiles a view needs, each paired with the rectangle to draw
    /// it into. Saves keeping two lists in step.
    /// VL.GIS's own implementation; TileIndex is BruTile's type, SKRect is SkiaSharp's.
    /// </summary>
    public static void VisibleTileLayout(
        MapView view,
        out IReadOnlyList<TileIndex> tileIndices,
        out IReadOnlyList<SKRect> destinations,
        int zoomLevel = -1)
    {
        var tiles = VisibleTiles(view, zoomLevel);
        var rects = new List<SKRect>(tiles.Count);
        foreach (var tile in tiles) rects.Add(TileDestination(view, tile));

        tileIndices  = tiles;
        destinations = rects;
    }

    /// <summary>
    /// Decode fetched tile bytes into an image ready to draw.
    /// Returns null for null or unreadable input, so a failed fetch does not throw.
    /// Uses SkiaSharp.
    /// </summary>
    public static SKImage? DecodeTile(byte[]? tileBytes)
    {
        if (tileBytes is null || tileBytes.Length == 0) return null;
        try
        {
            using var data = SKData.CreateCopy(tileBytes);
            return SKImage.FromEncodedData(data);
        }
        catch
        {
            return null;
        }
    }
}
