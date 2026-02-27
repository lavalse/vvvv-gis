# VL.GIS

A community GIS / geospatial library for [vvvv gamma](https://vvvv.org). Wraps mature .NET GIS libraries into vvvv nodes so you can work with maps, coordinates, geometries, and 3D terrain without leaving the visual patching environment.

**Status:** Phase 1 MVP — geometry, projection, serialization, tile fetching, and Stride 3D helpers are all implemented.

---

## Contents

- [What's included](#whats-included)
- [Prerequisites](#prerequisites)
- [Getting started](#getting-started)
  - [Option A — Direct DLL import (fastest for development)](#option-a--direct-dll-import-fastest-for-development)
  - [Option B — Local NuGet feed (for distribution)](#option-b--local-nuget-feed-for-distribution)
- [Node reference](#node-reference)
  - [GIS.Geometry](#gisgeometry)
  - [GIS.Projection](#gisprojection)
  - [GIS.Serialization](#gisserialization)
  - [GIS.Tiles](#gistiles)
  - [GIS.Stride.Coordinates](#gisstride-coordinates)
  - [GIS.Stride.Tessellation](#gisstride-tessellation)
  - [GIS.Stride.Elevation](#gisstride-elevation)
- [Key concepts](#key-concepts)
- [Example patches](#example-patches)
- [Dependencies](#dependencies)
- [Roadmap](#roadmap)

---

## What's included

| Assembly | Node category | Purpose |
|---|---|---|
| `VL.GIS.Core` | `GIS.Geometry` | Create and operate on NTS geometries (points, lines, polygons) |
| `VL.GIS.Core` | `GIS.Projection` | Reproject between coordinate reference systems (WGS84, Web Mercator, UTM, …) |
| `VL.GIS.Core` | `GIS.Serialization` | Parse/write WKT, WKB, GeoJSON |
| `VL.GIS.Tiles` | `GIS.Tiles` | Fetch map tiles from OSM, XYZ, and WMTS sources |
| `VL.GIS.Stride` | `GIS.Stride.Coordinates` | Convert GIS coordinates to float scene positions without precision loss |
| `VL.GIS.Stride` | `GIS.Stride.Tessellation` | Tessellate polygons and lines into Stride-ready triangle meshes |
| `VL.GIS.Stride` | `GIS.Stride.Elevation` | Heightmap creation, normalization, sampling, normal generation, terrain mesh |

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed on **Windows** (vvvv runs on Windows)
- [vvvv gamma 6.x or 7.x](https://visualprogramming.net)

> **WSL2 note:** The source code can live in WSL2, but the build and vvvv import steps happen on the Windows side. Access WSL files from Windows via `\\wsl$\<distro>\home\...`.

---

## Getting started

### Option A — Direct DLL import (fastest for development)

No packaging needed. Best while iterating.

**1. Build**

```powershell
# From the repo root on Windows (cmd / PowerShell / VS2022 terminal)
dotnet build VL.GIS.sln
```

Or use the included script which also packs:

```powershell
.\build.ps1          # Release build (default)
.\build.ps1 -Configuration Debug
```

The built DLLs land in:
```
src\VL.GIS.Core\bin\Debug\net8.0\VL.GIS.Core.dll
src\VL.GIS.Tiles\bin\Debug\net8.0\VL.GIS.Tiles.dll
src\VL.GIS.Stride\bin\Debug\net8.0\VL.GIS.Stride.dll
```

**2. Import into vvvv**

1. Open any patch in vvvv gamma
2. Quad menu → **Edit** → **Import .NET Assembly**
3. Browse to `VL.GIS.Core.dll` → Import
4. Repeat for `VL.GIS.Tiles.dll` and `VL.GIS.Stride.dll`
5. Press `Ctrl+N` in the NodeBrowser → type `GIS` → nodes should appear

All transitive dependencies (NTS, BruTile, ProjNet, etc.) are copied to the same `bin/` folder during build, so vvvv resolves them automatically.

---

### Option B — Local NuGet feed (for distribution)

Packages the library properly. Use this when sharing with others.

**1. Build and pack**

```powershell
.\build.ps1    # produces nupkg\VL.GIS.0.1.0.nupkg
```

**2. Add the local feed in vvvv**

1. Quad menu → **Edit** → **Manage NuGet Packages**
2. Click the gear icon → **Add package source**
3. Browse to the `nupkg\` folder, name it `VL.GIS local`
4. Search for **VL.GIS** → Install
5. Restart vvvv (or press F5) → `Ctrl+N` → type `GIS`

---

## Node reference

Every `public static method` on a `public class` in the C# source becomes a node in vvvv's NodeBrowser. Parameter names become pin labels (camelCase → "Camel Case"). XML doc comments become tooltips.

---

### GIS.Geometry

Source: `src/VL.GIS.Core/GeometryNodes.cs`

All geometries use WGS84 (EPSG:4326) by default. Coordinates are `(longitude, latitude)` — **longitude first**.

#### Creation

| Node | Inputs | Output | Description |
|---|---|---|---|
| `CreatePoint` | `longitude`, `latitude` | `Point` | Point from WGS84 lon/lat |
| `CreatePoint3D` | `longitude`, `latitude`, `elevation` | `Point` | Point with Z (metres) |
| `CreateLineString` | `points` (sequence of lon/lat tuples) | `LineString` | Ordered line |
| `CreatePolygon` | `exteriorRing` | `Polygon` | Polygon from ring; auto-closed |
| `CreatePolygonWithHoles` | `exteriorRing`, `holes` | `Polygon` | Polygon with interior holes |
| `CreateBoundingBox` | `minLongitude`, `minLatitude`, `maxLongitude`, `maxLatitude` | `Polygon` | Axis-aligned bbox polygon |

#### Spatial operations

| Node | Inputs | Output | Description |
|---|---|---|---|
| `Buffer` | `geometry`, `distance`, `segments` | `Geometry` | Expand/contract by distance (in CRS units) |
| `BufferWithStyle` | `geometry`, `distance`, `endCapStyle`, `segments` | `Geometry` | Buffer with flat/round/square caps |
| `Intersection` | `a`, `b` | `Geometry` | A ∩ B |
| `Union` | `a`, `b` | `Geometry` | A ∪ B |
| `Difference` | `a`, `b` | `Geometry` | A − B |
| `SymmetricDifference` | `a`, `b` | `Geometry` | A XOR B |
| `ConvexHull` | `geometry` | `Geometry` | Convex hull |
| `Centroid` | `geometry` | `Point` | Centroid point |
| `Envelope` | `geometry` | `Geometry` | Bounding box geometry |
| `Simplify` | `geometry`, `distanceTolerance` | `Geometry` | Douglas-Peucker simplification |

#### Predicates

| Node | Output | Description |
|---|---|---|
| `Intersects` | `bool` | A and B share any point |
| `Contains` | `bool` | A fully contains B |
| `Within` | `bool` | A is fully within B |
| `Touches` | `bool` | A and B share boundary point only |
| `Disjoint` | `bool` | A and B share no points |
| `Covers` | `bool` | A covers B (no point of B is outside A) |

#### Measurements

| Node | Output | Description |
|---|---|---|
| `Area` | `double` | Area in CRS units² |
| `Length` | `double` | Length/perimeter in CRS units |
| `Distance` | `double` | Min distance between two geometries |
| `GetGeometries` | `IReadOnlyList<Geometry>` | Decompose to component geometries |
| `GetCoordinates` | `IReadOnlyList<(lon, lat)>` | All coordinates as tuples |

---

### GIS.Projection

Source: `src/VL.GIS.Core/ProjectionNodes.cs`

#### Well-known CRS

| Node | Output | Description |
|---|---|---|
| `Wgs84` | `ICoordinateSystem` | EPSG:4326 — geographic lon/lat |
| `WebMercator` | `ICoordinateSystem` | EPSG:3857 — used by OSM/Google tiles |
| `CreateUtm` | `ICoordinateSystem` | UTM zone for metric calculations |
| `ParseWkt` | `ICoordinateSystem` | Parse any CRS from WKT string |

#### Reprojection

| Node | Inputs | Output | Description |
|---|---|---|---|
| `CreateTransformation` | `source`, `target` | `ICoordinateTransformation` | Build a reusable transform (cache this) |
| `ReprojectPoint` | `transformation`, `x`, `y` | `(x, y)` | Reproject a single coordinate pair |
| `ReprojectPoints` | `transformation`, `points` | `IReadOnlyList<(x, y)>` | Bulk reproject — more efficient than looping |
| `ReprojectPointGeometry` | `point`, `source`, `target` | `Point` | Reproject an NTS Point |
| `ReprojectGeometry` | `geometry`, `source`, `target` | `Geometry` | Reproject any NTS Geometry |

#### Utilities

| Node | Description |
|---|---|
| `UtmZoneFromLongitude` | Returns UTM zone 1–60 for a given longitude |
| `LonLatToWebMercator` | Fast WGS84 → EPSG:3857 (no ProjNet overhead) |
| `WebMercatorToLonLat` | Fast EPSG:3857 → WGS84 |

---

### GIS.Serialization

Source: `src/VL.GIS.Core/SerializationNodes.cs`

#### WKT (Well-Known Text)

| Node | Description |
|---|---|
| `ParseWkt` | Parse geometry from WKT string, e.g. `"POINT(13.4 52.5)"` |
| `TryParseWkt` | Parse without throwing; outputs `bool` success flag |
| `ToWkt` | Serialize geometry to WKT |

#### WKB (Well-Known Binary)

| Node | Description |
|---|---|
| `ParseWkb` | Parse from `byte[]` |
| `ToWkb` | Serialize to `byte[]` |
| `ParseHexWkb` | Parse from hex string (PostGIS format) |
| `ToHexWkb` | Serialize to hex string |

#### GeoJSON

| Node | Description |
|---|---|
| `ParseGeoJsonGeometry` | Parse GeoJSON geometry object string |
| `TryParseGeoJsonGeometry` | Parse without throwing |
| `ToGeoJsonGeometry` | Serialize to GeoJSON geometry string |

#### Bounding box

| Node | Outputs | Description |
|---|---|---|
| `GetBoundingBox` | `minLon`, `minLat`, `maxLon`, `maxLat` | Extract bbox of any geometry |
| `BoundingBoxCenter` | `(longitude, latitude)` | Center of the bbox |

---

### GIS.Tiles

Sources: `src/VL.GIS.Tiles/TileProviderNodes.cs`, `TileFetchNodes.cs`

Tiles use Web Mercator (EPSG:3857) / XYZ (slippy map) convention. Zoom level 0 = whole world; zoom 19 = street level.

#### Tile sources

| Node | Description |
|---|---|
| `OsmTileSource` | OpenStreetMap standard tiles (zoom 0–19) |
| `OpenTopoMapTileSource` | Topographic map (zoom 0–17) |
| `XyzTileSource` | Custom XYZ source from URL template `{z}/{x}/{y}` |
| `WmtsTileSource` | WMTS source from a GetCapabilities URL + layer ID |
| `TileSchemaName` | Get schema name from a tile source |
| `TileSchemaZoomRange` | Get min/max zoom levels |
| `TileAttribution` | Get attribution text (display to users as required) |

#### Tile indexing

| Node | Description |
|---|---|
| `CreateTileIndex` | Create index from explicit col/row/level |
| `TileIndexFromLonLat` | Convert lon/lat + zoom to tile index |
| `TileIndicesForBounds` | List all tile indices covering a bbox at a zoom level |
| `TileBounds` | Convert a tile index back to WGS84 bbox |

#### Fetching

| Node | Output | Description |
|---|---|---|
| `FetchTileBytes` | `byte[]?` | Fetch a tile synchronously (null on failure) |
| `FetchTileToFile` | `string?` | Fetch and write to disk; returns file path |
| `FetchTileAsync` | `IObservable<byte[]?>` | Fetch asynchronously — connect to **S+H** in vvvv |
| `FetchTilesAsync` | `IObservable<(TileIndex, byte[]?)>` | Fetch multiple tiles in parallel |
| `CreateFileCache` | `FileCache` | Persistent on-disk tile cache |
| `IsTileCached` | `bool` | Check presence in cache without fetching |

---

### GIS.Stride.Coordinates

Source: `src/VL.GIS.Stride/CoordinateConverter.cs`

Solves the **float precision problem**: WGS84 coordinates at global scale exceed float32 precision, causing visible jitter. Solution: define a scene origin in double precision, then store all positions as float offsets relative to that origin.

| Node | Description |
|---|---|
| `CreateSceneOrigin` | Define a lon/lat reference point for the scene (store as persistent node) |
| `LonLatToLocal` | WGS84 → `Vector3` local position. 1 unit = 1 metre. +Y = up, +Z = south. Accurate within ~50 km of origin. |
| `LocalToLonLat` | Inverse of `LonLatToLocal` |
| `CreateWebMercatorOrigin` | Like `CreateSceneOrigin` but in EPSG:3857 metres |
| `WebMercatorToLocal` | EPSG:3857 → `Vector3` local position |
| `MetresPerDegreeLongitude` | Approx. metres/degree at a given latitude (lon direction) |
| `MetresPerDegreeLatitude` | Approx. metres/degree in latitude direction (~111 319 m) |

---

### GIS.Stride.Tessellation

Source: `src/VL.GIS.Stride/GeometryTessellator.cs`

Converts NTS geometries into triangle mesh data (flat `Vector3[]` + `int[]`) ready for Stride's mesh API. **Convert coordinates with `CoordinateConverter` before tessellating.**

| Node | Outputs | Description |
|---|---|---|
| `TessellatePolygon` | `positions`, `indices` | Polygon → Delaunay triangle mesh. Holes are respected. |
| `TessellateMultiPolygon` | `positions`, `indices` | MultiPolygon → merged triangle mesh |
| `LineStringToPositions` | `IReadOnlyList<Vector3>` | LineString → ordered position list for line rendering |
| `LineStringToRibbonMesh` | `positions`, `indices` | LineString → flat ribbon mesh with given width (metres) |
| `CreateTileQuad` | `positions`, `uvs`, `indices` | Axis-aligned textured quad for a map tile |

---

### GIS.Stride.Elevation

Source: `src/VL.GIS.Stride/ElevationNodes.cs`

Works with DEM (Digital Elevation Model) data as flat `float[]` arrays in row-major order.

| Node | Description |
|---|---|
| `CreateFlatHeightmap` | Allocate a flat (all-zero) heightmap of given dimensions |
| `HeightmapFromArray` | Convert `float[][]` 2D grid to flat row-major array |
| `NormalizeHeightmap` | Rescale to 0–1 range; outputs `minElevation` and `maxElevation` |
| `SampleHeightmap` | Bilinear sample at fractional UV coordinates (0–1) |
| `GenerateNormals` | Central-differences normal map from heightmap → `Vector3[]` |
| `HeightmapToMesh` | Full terrain mesh: positions + UVs + indices from heightmap |

---

## Key concepts

### Coordinate order

NTS / WGS84 convention: **longitude first, then latitude** — the same as `(x, y)` / `(east, north)`. This is the opposite of how coordinates are often spoken ("lat, lon"), so watch out.

### CRS units

- WGS84 (EPSG:4326): degrees. `Buffer(geom, 0.001)` ≈ 111 m at the equator.
- Web Mercator (EPSG:3857): metres.
- UTM: metres. Best for accurate distance/area calculations within a zone.

### Tile attribution

When showing OSM tiles publicly, you **must** display attribution:
> © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors

Use `TileAttribution` to retrieve the string from the tile source.

### Async tiles in vvvv

`FetchTileAsync` returns an `IObservable<byte[]?>`. In a vvvv patch, wire it through a **S+H (Sample & Hold)** node to latch the bytes once they arrive. Retrieve tile bytes on-demand per frame by toggling the S+H.

### Float precision

Always use `CoordinateConverter.LonLatToLocal` with a stable scene origin when placing geometry in a Stride scene. The equirectangular approximation it uses is accurate to within ~1 m at 50 km from the origin. For larger areas, place multiple scene origins.

---

## Example patches

The `help/` folder contains three starter patches:

| Patch | What it shows |
|---|---|
| `HowTo OSM Tile Viewer.vl` | Fetch OSM tiles and display them as textured quads |
| `HowTo GeoJSON Polygon Buffer.vl` | Parse GeoJSON, run a buffer operation, display the result |
| `HowTo Stride Terrain.vl` | Build a terrain mesh from a heightmap with normals |

---

## Dependencies

| Library | Version | License | Purpose |
|---|---|---|---|
| [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) | 2.6.0 | BSD | Geometry model and spatial operations |
| [ProjNet](https://github.com/NetTopologySuite/ProjNet4GeoAPI) | 2.1.0 | Apache 2.0 | Coordinate reference system transformations |
| [BruTile](https://github.com/BruTile/BruTile) | 6.0.0 | Apache 2.0 | Tile source abstraction and fetching |
| [System.Reactive](https://github.com/dotnet/reactive) | 6.0.1 | MIT | `IObservable` for async tile results |

---

## Roadmap

| Phase | Status | Description |
|---|---|---|
| 1 — Core | ✅ Done | Geometry, projection, serialization, tiles, Stride 3D helpers |
| 2 — 2D rendering | Planned | VL.Skia integration: tiles as `SKBitmap`, vectors as `SKPath` |
| 3 — Full 3D | Planned | Tile quad scenes, triangulated meshes, DEM heightmaps in Stride |
| 4 — File I/O | Planned | GeoTIFF, Shapefile, KML via MaxRev.Gdal.Core |

---

## License

MIT. See [LICENSE](LICENSE) if present, or assume MIT until a license file is added.

Tile data © respective providers. OSM data © OpenStreetMap contributors (ODbL).
