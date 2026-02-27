using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace VL.GIS.Stride;

/// <summary>
/// Tessellates NTS geometry (polygons, lines) into mesh data for Stride3D.
///
/// Output: flat arrays of Vector3 positions and int indices, ready to pass
/// to Stride's GeometricPrimitive or a custom MeshDraw builder.
///
/// Category: GIS.Stride.Tessellation
/// </summary>
public static class GeometryTessellator
{
    // ── Polygon → Triangle Mesh ───────────────────────────────────────────────

    /// <summary>
    /// Tessellate a Polygon into a flat triangle mesh.
    /// Uses Delaunay constrained triangulation (NTS DelaunayTriangulationBuilder).
    ///
    /// outputPositions: flat list of Vector3 vertices.
    /// outputIndices:   flat list of int triangle indices (CCW winding).
    ///
    /// Coordinates must already be in scene-local space (use CoordinateConverter first).
    /// </summary>
    public static void TessellatePolygon(
        Polygon polygon,
        double originLon, double originLat,
        out IReadOnlyList<Vector3> outputPositions,
        out IReadOnlyList<int> outputIndices,
        float elevationY = 0f)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();

        // Build constrained Delaunay triangulation
        var builder = new DelaunayTriangulationBuilder();
        builder.SetSites(polygon);
        var triangles = builder.GetTriangles(polygon.Factory);

        for (int i = 0; i < triangles.NumGeometries; i++)
        {
            var tri = (Polygon)triangles.GetGeometryN(i);
            var coords = tri.Coordinates;
            if (coords.Length < 4) continue; // degenerate

            // Only include triangles whose centroid is inside the input polygon
            var centroid = tri.Centroid;
            if (!polygon.Contains(centroid)) continue;

            int baseIndex = positions.Count;
            for (int v = 0; v < 3; v++)
            {
                var pos = CoordinateConverter.LonLatToLocal(
                    coords[v].X, coords[v].Y,
                    originLon, originLat,
                    elevationY);
                positions.Add(pos);
            }

            // CCW winding (Stride uses left-hand coordinate system)
            indices.Add(baseIndex);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);
        }

        outputPositions = positions;
        outputIndices = indices;
    }

    /// <summary>
    /// Tessellate a MultiPolygon into a single merged triangle mesh.
    /// </summary>
    public static void TessellateMultiPolygon(
        MultiPolygon multiPolygon,
        double originLon, double originLat,
        out IReadOnlyList<Vector3> outputPositions,
        out IReadOnlyList<int> outputIndices,
        float elevationY = 0f)
    {
        var allPositions = new List<Vector3>();
        var allIndices = new List<int>();

        for (int i = 0; i < multiPolygon.NumGeometries; i++)
        {
            if (multiPolygon.GetGeometryN(i) is not Polygon poly) continue;

            TessellatePolygon(poly, originLon, originLat,
                out var polyPositions, out var polyIndices, elevationY);

            int offset = allPositions.Count;
            allPositions.AddRange(polyPositions);
            foreach (var idx in polyIndices)
                allIndices.Add(idx + offset);
        }

        outputPositions = allPositions;
        outputIndices = allIndices;
    }

    // ── LineString → Line Mesh ────────────────────────────────────────────────

    /// <summary>
    /// Convert a LineString into a series of Vector3 positions for line rendering.
    /// Use with Stride's line rendering or a custom tube mesh.
    /// </summary>
    public static IReadOnlyList<Vector3> LineStringToPositions(
        LineString lineString,
        double originLon, double originLat,
        float elevationY = 0f)
    {
        var positions = new List<Vector3>(lineString.NumPoints);
        foreach (var coord in lineString.Coordinates)
        {
            positions.Add(CoordinateConverter.LonLatToLocal(
                coord.X, coord.Y,
                originLon, originLat,
                elevationY));
        }
        return positions;
    }

    /// <summary>
    /// Convert a LineString into a flat ribbon mesh (two triangles per segment).
    /// width: ribbon width in metres.
    /// </summary>
    public static void LineStringToRibbonMesh(
        LineString lineString,
        double originLon, double originLat,
        float width,
        out IReadOnlyList<Vector3> outputPositions,
        out IReadOnlyList<int> outputIndices,
        float elevationY = 0f)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();

        var pts = lineString.Coordinates;
        float halfWidth = width * 0.5f;

        for (int i = 0; i < pts.Length - 1; i++)
        {
            var a = CoordinateConverter.LonLatToLocal(
                pts[i].X, pts[i].Y, originLon, originLat, elevationY);
            var b = CoordinateConverter.LonLatToLocal(
                pts[i + 1].X, pts[i + 1].Y, originLon, originLat, elevationY);

            // Perpendicular in XZ plane
            var dir = Vector3.Normalize(b - a);
            var perp = new Vector3(-dir.Z, 0, dir.X) * halfWidth;

            int baseIdx = positions.Count;
            positions.Add(a - perp); // 0
            positions.Add(a + perp); // 1
            positions.Add(b + perp); // 2
            positions.Add(b - perp); // 3

            // Two triangles (CCW)
            indices.Add(baseIdx);
            indices.Add(baseIdx + 1);
            indices.Add(baseIdx + 2);

            indices.Add(baseIdx);
            indices.Add(baseIdx + 2);
            indices.Add(baseIdx + 3);
        }

        outputPositions = positions;
        outputIndices = indices;
    }

    // ── Tile Quad Mesh ────────────────────────────────────────────────────────

    /// <summary>
    /// Create a textured quad mesh for a map tile.
    /// The quad spans from (minX, 0, minZ) to (maxX, 0, maxZ) in local scene space.
    ///
    /// outputPositions: 4 vertices (corners of the quad).
    /// outputUVs:       4 UV coordinates (0,0)→(1,1).
    /// outputIndices:   6 indices (two triangles, CCW).
    /// </summary>
    public static void CreateTileQuad(
        float minX, float minZ,
        float maxX, float maxZ,
        float elevationY,
        out IReadOnlyList<Vector3> outputPositions,
        out IReadOnlyList<Vector2> outputUVs,
        out IReadOnlyList<int> outputIndices)
    {
        outputPositions = new[]
        {
            new Vector3(minX, elevationY, minZ), // SW
            new Vector3(maxX, elevationY, minZ), // SE
            new Vector3(maxX, elevationY, maxZ), // NE
            new Vector3(minX, elevationY, maxZ), // NW
        };

        outputUVs = new[]
        {
            new Vector2(0, 1), // SW
            new Vector2(1, 1), // SE
            new Vector2(1, 0), // NE
            new Vector2(0, 0), // NW
        };

        outputIndices = new[] { 0, 1, 2, 0, 2, 3 };
    }
}
