using System;
using System.Collections.Generic;
using System.Numerics;

namespace VL.GIS.Stride;

/// <summary>
/// Elevation / heightmap utilities for 3D terrain rendering in Stride.
///
/// Stride's Heightmap component accepts float[] arrays directly.
/// These helpers convert DEM (Digital Elevation Model) data into the
/// format expected by Stride.
///
/// Category: GIS.Stride.Elevation
/// </summary>
public static class ElevationNodes
{
    // ── Heightmap Creation ────────────────────────────────────────────────────

    /// <summary>
    /// Create a flat float[] heightmap array of given dimensions, all zeros.
    /// Size: width × height floats, row-major (X = column, Y = row).
    /// </summary>
    public static float[] CreateFlatHeightmap(int width, int height)
        => new float[width * height];

    /// <summary>
    /// Create a heightmap from a 2D array of elevation values (in metres).
    /// input[row][col] → output float[] (row-major).
    /// </summary>
    public static float[] HeightmapFromArray(float[][] elevationGrid)
    {
        int rows = elevationGrid.Length;
        if (rows == 0) return Array.Empty<float>();
        int cols = elevationGrid[0].Length;
        var result = new float[rows * cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                result[r * cols + c] = elevationGrid[r][c];
        return result;
    }

    /// <summary>
    /// Normalize a heightmap to the 0–1 range (Stride requires this for some modes).
    /// Also outputs the min and max elevation for de-normalization.
    /// </summary>
    public static float[] NormalizeHeightmap(
        float[] heightmap,
        out float minElevation,
        out float maxElevation)
    {
        minElevation = float.MaxValue;
        maxElevation = float.MinValue;
        foreach (float h in heightmap)
        {
            if (h < minElevation) minElevation = h;
            if (h > maxElevation) maxElevation = h;
        }

        float range = maxElevation - minElevation;
        if (range == 0f) return new float[heightmap.Length]; // all zeros

        var result = new float[heightmap.Length];
        for (int i = 0; i < heightmap.Length; i++)
            result[i] = (heightmap[i] - minElevation) / range;
        return result;
    }

    /// <summary>
    /// Sample a heightmap at a fractional (u, v) coordinate using bilinear interpolation.
    /// u and v are in [0, 1] range.
    /// </summary>
    public static float SampleHeightmap(float[] heightmap, int width, int height, float u, float v)
    {
        float px = u * (width - 1);
        float py = v * (height - 1);
        int x0 = Math.Clamp((int)Math.Floor(px), 0, width - 1);
        int y0 = Math.Clamp((int)Math.Floor(py), 0, height - 1);
        int x1 = Math.Min(x0 + 1, width - 1);
        int y1 = Math.Min(y0 + 1, height - 1);
        float tx = px - x0;
        float ty = py - y0;

        float h00 = heightmap[y0 * width + x0];
        float h10 = heightmap[y0 * width + x1];
        float h01 = heightmap[y1 * width + x0];
        float h11 = heightmap[y1 * width + x1];

        return h00 * (1 - tx) * (1 - ty)
             + h10 * tx * (1 - ty)
             + h01 * (1 - tx) * ty
             + h11 * tx * ty;
    }

    // ── Normal Map ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate a normal map from a heightmap using central differences.
    /// Returns Vector3[] normals (unit length, pointing upward from terrain).
    /// cellSizeMetres: horizontal distance between adjacent heightmap cells.
    /// </summary>
    public static Vector3[] GenerateNormals(float[] heightmap, int width, int height, float cellSizeMetres = 1f)
    {
        var normals = new Vector3[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int xl = Math.Max(x - 1, 0);
                int xr = Math.Min(x + 1, width - 1);
                int yd = Math.Max(y - 1, 0);
                int yu = Math.Min(y + 1, height - 1);

                float dzdx = (heightmap[y * width + xr] - heightmap[y * width + xl]) / (2f * cellSizeMetres);
                float dzdy = (heightmap[yu * width + x] - heightmap[yd * width + x]) / (2f * cellSizeMetres);

                var n = Vector3.Normalize(new Vector3(-dzdx, 1f, -dzdy));
                normals[y * width + x] = n;
            }
        }
        return normals;
    }

    // ── Terrain Mesh ─────────────────────────────────────────────────────────

    /// <summary>
    /// Generate a terrain mesh from a heightmap.
    /// Creates a grid of (width × height) vertices with triangle list indices.
    /// scaleX, scaleZ: metres per cell in X and Z directions.
    /// </summary>
    public static void HeightmapToMesh(
        float[] heightmap, int width, int height,
        float scaleX, float scaleZ,
        out IReadOnlyList<Vector3> outputPositions,
        out IReadOnlyList<Vector2> outputUVs,
        out IReadOnlyList<int> outputIndices)
    {
        var positions = new List<Vector3>(width * height);
        var uvs = new List<Vector2>(width * height);
        var indices = new List<int>((width - 1) * (height - 1) * 6);

        float offsetX = -(width - 1) * scaleX * 0.5f;
        float offsetZ = -(height - 1) * scaleZ * 0.5f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = heightmap[y * width + x];
                positions.Add(new Vector3(offsetX + x * scaleX, h, offsetZ + y * scaleZ));
                uvs.Add(new Vector2((float)x / (width - 1), (float)y / (height - 1)));
            }
        }

        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int i = y * width + x;
                // Upper-left triangle
                indices.Add(i);
                indices.Add(i + width);
                indices.Add(i + 1);
                // Lower-right triangle
                indices.Add(i + 1);
                indices.Add(i + width);
                indices.Add(i + width + 1);
            }
        }

        outputPositions = positions;
        outputUVs = uvs;
        outputIndices = indices;
    }
}
