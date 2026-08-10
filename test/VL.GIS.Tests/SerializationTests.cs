using VL.GIS;

namespace VL.GIS.Tests;

public class SerializationTests
{
    const double Lon = 139.7;
    const double Lat = 35.68;

    [Fact]
    public void ToWkt_writes_longitude_first()
    {
        // The exact string the first help patch shows. Pinned verbatim, because the format
        // is what a user copies into PostGIS or a text file, and a change to it is a
        // breaking change even though nothing throws.
        var wkt = SerializationNodes.ToWkt(GeometryNodes.CreatePoint(Lon, Lat));

        Assert.Equal("POINT (139.7 35.68)", wkt);
    }

    [Fact]
    public void ParseWkt_round_trips()
    {
        var original = GeometryNodes.CreatePoint(Lon, Lat);
        var parsed   = SerializationNodes.ParseWkt(SerializationNodes.ToWkt(original));

        Assert.True(original.EqualsExact(parsed));
    }

    [Fact]
    public void TryParseWkt_rejects_nonsense_without_throwing()
    {
        Assert.False(SerializationNodes.TryParseWkt("not a geometry", out var geometry));
        Assert.Null(geometry);

        Assert.True(SerializationNodes.TryParseWkt("POINT (1 2)", out var valid));
        Assert.NotNull(valid);
    }

    [Fact]
    public void ParseWkt_keeps_polygons_intact()
    {
        var polygon = GeometryNodes.CreatePolygon(new[]
        {
            (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)
        });

        var parsed = SerializationNodes.ParseWkt(SerializationNodes.ToWkt(polygon));

        Assert.Equal(GeometryNodes.Area(polygon), GeometryNodes.Area(parsed), 12);
        Assert.Equal(polygon.Coordinates.Length, parsed.Coordinates.Length);
    }

    [Fact]
    public void Wkb_round_trips()
    {
        var original = GeometryNodes.CreatePoint(Lon, Lat);
        var parsed   = SerializationNodes.ParseWkb(SerializationNodes.ToWkb(original));

        Assert.True(original.EqualsExact(parsed));
    }

    [Fact]
    public void HexWkb_round_trips_and_is_the_hex_of_the_binary_form()
    {
        var original = GeometryNodes.CreatePoint(Lon, Lat);

        var hex   = SerializationNodes.ToHexWkb(original);
        var bytes = SerializationNodes.ToWkb(original);

        Assert.Equal(bytes.Length * 2, hex.Length);
        Assert.Matches("^[0-9A-Fa-f]+$", hex);
        Assert.True(original.EqualsExact(SerializationNodes.ParseHexWkb(hex)));
    }

    [Fact]
    public void GeoJson_round_trips()
    {
        // This is the path that drags in Newtonsoft.Json, which vvvv logs as superseded by
        // its own 13.0.3. Worth having covered before the planned move to GeoJSON4STJ.
        var original = GeometryNodes.CreatePoint(Lon, Lat);

        var json   = SerializationNodes.ToGeoJsonGeometry(original);
        var parsed = SerializationNodes.ParseGeoJsonGeometry(json);

        Assert.Contains("\"Point\"", json);
        Assert.Contains("139.7", json);
        Assert.Equal(Lon, parsed.Coordinates[0].X, 9);
        Assert.Equal(Lat, parsed.Coordinates[0].Y, 9);
    }

    [Fact]
    public void GeoJson_puts_longitude_first_as_the_spec_requires()
    {
        // RFC 7946 is explicit that position is [longitude, latitude]. Getting this the
        // wrong way round would make our output silently unreadable by every other tool.
        var json = SerializationNodes.ToGeoJsonGeometry(GeometryNodes.CreatePoint(Lon, Lat));

        int lonAt = json.IndexOf("139.7", StringComparison.Ordinal);
        int latAt = json.IndexOf("35.68", StringComparison.Ordinal);

        Assert.True(lonAt >= 0 && latAt >= 0);
        Assert.True(lonAt < latAt, $"longitude must precede latitude in {json}");
    }

    [Fact]
    public void TryParseGeoJsonGeometry_rejects_nonsense_without_throwing()
    {
        Assert.False(SerializationNodes.TryParseGeoJsonGeometry("{ nope }", out var geometry));
        Assert.Null(geometry);
    }

    [Fact]
    public void GetBoundingBox_returns_the_envelope_corners()
    {
        var polygon = GeometryNodes.CreatePolygon(new[]
        {
            (139.0, 35.0), (140.0, 35.0), (140.0, 36.0), (139.0, 36.0)
        });

        SerializationNodes.GetBoundingBox(polygon,
            out double minLon, out double minLat, out double maxLon, out double maxLat);

        Assert.Equal(139.0, minLon, 9);
        Assert.Equal(35.0, minLat, 9);
        Assert.Equal(140.0, maxLon, 9);
        Assert.Equal(36.0, maxLat, 9);
    }

    [Fact]
    public void BoundingBoxCenter_is_the_middle_of_the_envelope()
    {
        var polygon = GeometryNodes.CreatePolygon(new[]
        {
            (139.0, 35.0), (140.0, 35.0), (140.0, 36.0), (139.0, 36.0)
        });

        // Returns a (longitude, latitude) tuple, not a Point, so the pins in vvvv are two
        // separate outputs named Longitude and Latitude.
        var centre = SerializationNodes.BoundingBoxCenter(polygon);

        Assert.Equal(139.5, centre.longitude, 9);
        Assert.Equal(35.5, centre.latitude, 9);
    }
}
