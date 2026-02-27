using System;
using System.Numerics;

namespace VL.GIS.Stride;

/// <summary>
/// Coordinate conversion utilities for placing GIS data in a 3D Stride scene.
///
/// The key challenge is float precision: WGS84 coordinates have ~7 decimal places
/// of meaningful data, but a float32 only has ~7 significant digits total.
/// At global scale this causes visible "jitter" when objects move.
///
/// Solution: store a reference origin as double, then convert all points to
/// local float offsets relative to that origin. The scene origin sits at (0,0,0).
///
/// Category: GIS.Stride.Coordinates
/// </summary>
public static class CoordinateConverter
{
    // ── Reference Origin ──────────────────────────────────────────────────────

    /// <summary>
    /// Create a scene origin from a longitude/latitude reference point.
    /// All subsequent conversions subtract this origin before converting to float.
    /// Store this as a persistent node in your vvvv patch.
    /// </summary>
    public static (double originLon, double originLat) CreateSceneOrigin(
        double longitude, double latitude)
        => (longitude, latitude);

    // ── WGS84 → Local Float ───────────────────────────────────────────────────

    /// <summary>
    /// Convert a WGS84 (lon/lat) point to a scene-local Vector3 position.
    /// Uses equirectangular approximation — accurate within ~50 km of origin.
    /// Scale: 1 unit = 1 metre.
    /// Y axis = up (Stride convention).
    /// </summary>
    public static Vector3 LonLatToLocal(
        double longitude, double latitude,
        double originLon, double originLat,
        double elevation = 0.0)
    {
        const double MetresPerDegree = 111_319.491; // at equator

        double dLon = longitude - originLon;
        double dLat = latitude - originLat;

        // Scale longitude by cos(lat) to account for meridian convergence
        double cosLat = Math.Cos(originLat * Math.PI / 180.0);

        double x = (float)(dLon * MetresPerDegree * cosLat);
        double z = (float)(-dLat * MetresPerDegree); // Stride: +Z = south
        double y = elevation;

        return new Vector3((float)x, (float)y, (float)z);
    }

    /// <summary>
    /// Convert a scene-local Vector3 back to WGS84 longitude/latitude.
    /// </summary>
    public static (double longitude, double latitude) LocalToLonLat(
        Vector3 localPosition,
        double originLon, double originLat)
    {
        const double MetresPerDegree = 111_319.491;
        double cosLat = Math.Cos(originLat * Math.PI / 180.0);

        double lon = originLon + localPosition.X / (MetresPerDegree * cosLat);
        double lat = originLat - localPosition.Z / MetresPerDegree;
        return (lon, lat);
    }

    // ── Web Mercator → Local Float ────────────────────────────────────────────

    /// <summary>
    /// Convert Web Mercator (EPSG:3857) meters to scene-local Vector3.
    /// Suitable when your data is already in EPSG:3857 (tile coordinates, etc.).
    /// Scale: 1 unit = 1 metre.
    /// </summary>
    public static Vector3 WebMercatorToLocal(
        double mercatorX, double mercatorY,
        double originMercatorX, double originMercatorY,
        double elevation = 0.0)
    {
        float x = (float)(mercatorX - originMercatorX);
        float z = (float)(-(mercatorY - originMercatorY)); // flip Y→Z, +Z = south
        float y = (float)elevation;
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Create a Web Mercator origin from a longitude/latitude reference point.
    /// Converts to EPSG:3857 meters for use with WebMercatorToLocal.
    /// </summary>
    public static (double originX, double originY) CreateWebMercatorOrigin(
        double longitude, double latitude)
    {
        const double R = 6_378_137.0;
        double x = longitude * Math.PI / 180.0 * R;
        double y = Math.Log(Math.Tan(Math.PI / 4.0 + latitude * Math.PI / 360.0)) * R;
        return (x, y);
    }

    // ── Scale Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Approximate metres-per-degree at a given latitude (longitude direction).
    /// Useful for setting up scale factors.
    /// </summary>
    public static double MetresPerDegreeLongitude(double latitude)
    {
        const double MetresPerDegree = 111_319.491;
        return MetresPerDegree * Math.Cos(latitude * Math.PI / 180.0);
    }

    /// <summary>
    /// Approximate metres-per-degree in the latitude direction (nearly constant).
    /// </summary>
    public static double MetresPerDegreeLatitude() => 111_319.491;
}
