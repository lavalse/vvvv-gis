using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System;

namespace VL.GIS.Core;

/// <summary>
/// Geometry serialization/deserialization nodes for vvvv.
/// Supports WKT, WKB, and GeoJSON formats.
/// Category: GIS.Serialization
/// </summary>
public static class SerializationNodes
{
    private static readonly WKTReader WktReader = new WKTReader();
    private static readonly WKTWriter WktWriter = new WKTWriter();
    private static readonly WKBReader WkbReader = new WKBReader();
    private static readonly WKBWriter WkbWriter = new WKBWriter();
    private static readonly GeoJsonReader GeoJsonReader = new GeoJsonReader();
    private static readonly GeoJsonWriter GeoJsonWriter = new GeoJsonWriter();

    // ── WKT ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a geometry from a WKT (Well-Known Text) string.
    /// Examples: "POINT(13.4 52.5)", "POLYGON((0 0, 1 0, 1 1, 0 1, 0 0))"
    /// </summary>
    public static Geometry ParseWkt(string wkt) => WktReader.Read(wkt);

    /// <summary>
    /// Try to parse a WKT string; outputs success flag instead of throwing.
    /// </summary>
    public static bool TryParseWkt(string wkt, out Geometry? geometry)
    {
        try
        {
            geometry = WktReader.Read(wkt);
            return true;
        }
        catch
        {
            geometry = null;
            return false;
        }
    }

    /// <summary>Serialize a geometry to its WKT representation.</summary>
    public static string ToWkt(Geometry geometry) => WktWriter.Write(geometry);

    // ── WKB ───────────────────────────────────────────────────────────────────

    /// <summary>Parse a geometry from a WKB (Well-Known Binary) byte array.</summary>
    public static Geometry ParseWkb(byte[] wkb) => WkbReader.Read(wkb);

    /// <summary>Serialize a geometry to WKB bytes.</summary>
    public static byte[] ToWkb(Geometry geometry) => WkbWriter.Write(geometry);

    /// <summary>
    /// Parse a geometry from a hex-encoded WKB string (PostGIS format).
    /// Example: "0101000000000000000000F03F0000000000000040"
    /// </summary>
    public static Geometry ParseHexWkb(string hexWkb)
        => WkbReader.Read(Convert.FromHexString(hexWkb));

    /// <summary>Serialize a geometry to a hex-encoded WKB string (PostGIS format).</summary>
    public static string ToHexWkb(Geometry geometry)
        => Convert.ToHexString(WkbWriter.Write(geometry));

    // ── GeoJSON ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a geometry from a GeoJSON geometry string.
    /// Input should be a GeoJSON Geometry object (not a Feature or FeatureCollection).
    /// Example: {"type":"Point","coordinates":[13.4,52.5]}
    /// </summary>
    public static Geometry ParseGeoJsonGeometry(string geoJson)
        => GeoJsonReader.Read<Geometry>(geoJson);

    /// <summary>
    /// Try to parse a GeoJSON geometry string; outputs success flag instead of throwing.
    /// </summary>
    public static bool TryParseGeoJsonGeometry(string geoJson, out Geometry? geometry)
    {
        try
        {
            geometry = GeoJsonReader.Read<Geometry>(geoJson);
            return true;
        }
        catch
        {
            geometry = null;
            return false;
        }
    }

    /// <summary>Serialize a geometry to a GeoJSON geometry string.</summary>
    public static string ToGeoJsonGeometry(Geometry geometry)
        => GeoJsonWriter.Write(geometry);

    // ── Bounding Box ──────────────────────────────────────────────────────────

    /// <summary>
    /// Extract the bounding box of a geometry as min/max lon/lat.
    /// </summary>
    public static void GetBoundingBox(
        Geometry geometry,
        out double minLongitude, out double minLatitude,
        out double maxLongitude, out double maxLatitude)
    {
        var env = geometry.EnvelopeInternal;
        minLongitude = env.MinX;
        minLatitude = env.MinY;
        maxLongitude = env.MaxX;
        maxLatitude = env.MaxY;
    }

    /// <summary>Return the bounding box center as (longitude, latitude).</summary>
    public static (double longitude, double latitude) BoundingBoxCenter(Geometry geometry)
    {
        var env = geometry.EnvelopeInternal;
        return (env.Centre.X, env.Centre.Y);
    }
}
