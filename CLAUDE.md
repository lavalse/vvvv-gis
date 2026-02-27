# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**VL.GIS** is a community GIS/geospatial library for [vvvv gamma](https://vvvv.org) (a .NET 8 visual programming environment). It wraps mature .NET GIS libraries into vvvv nodes, following the `VL.*` package naming convention.

**Phase 1 (MVP) is implemented.** The library provides:
- Geometry creation and spatial operations (NetTopologySuite)
- Coordinate reprojection between CRS (ProjNet)
- WKT / WKB / GeoJSON serialization
- Map tile fetching from OSM, WMTS, XYZ sources (BruTile)
- 3D Stride helpers: coordinate conversion, polygon tessellation, heightmap utilities

## Repository Structure

```
VL.GIS/
├── VL.GIS.sln
├── VL.GIS.vl              # vvvv entry point (NuGet deps + node documentation)
├── VL.GIS.nuspec          # NuGet package metadata
├── src/
│   ├── VL.GIS.Core/       # NTS + ProjNet wrappers
│   │   ├── GeometryNodes.cs
│   │   ├── ProjectionNodes.cs
│   │   └── SerializationNodes.cs
│   ├── VL.GIS.Tiles/      # BruTile wrappers
│   │   ├── TileProviderNodes.cs
│   │   └── TileFetchNodes.cs
│   └── VL.GIS.Stride/     # 3D mesh generation helpers
│       ├── CoordinateConverter.cs
│       ├── GeometryTessellator.cs
│       └── ElevationNodes.cs
└── help/                  # Example .vl patches
    ├── HowTo OSM Tile Viewer.vl
    ├── HowTo GeoJSON Polygon Buffer.vl
    └── HowTo Stride Terrain.vl
```

## Commands

### Prerequisites
- .NET 8 SDK (`dotnet --version` should show 8.x)
- vvvv gamma 7.0 (download from vvvv.org)

### Build

```bash
# Build all projects
dotnet build VL.GIS.sln

# Build Release
dotnet build VL.GIS.sln -c Release

# Build a single project
dotnet build src/VL.GIS.Core/VL.GIS.Core.csproj
```

### Pack NuGet

```bash
dotnet pack VL.GIS.sln -c Release -o nupkg/
```

### Restore

```bash
dotnet restore VL.GIS.sln
```

## Architecture

### vvvv Node Convention
Every `public static method` on a `public class` auto-becomes a vvvv node:
- `camelCase` parameter names → "Camel Case" pin labels
- `out` parameters → multiple output pins
- XML doc comment → node tooltip

### Node Categories (as they appear in vvvv NodeBrowser)
| Category | Source file |
|---|---|
| `GIS.Geometry` | `VL.GIS.Core/GeometryNodes.cs` |
| `GIS.Projection` | `VL.GIS.Core/ProjectionNodes.cs` |
| `GIS.Serialization` | `VL.GIS.Core/SerializationNodes.cs` |
| `GIS.Tiles` | `VL.GIS.Tiles/TileProviderNodes.cs`, `TileFetchNodes.cs` |
| `GIS.Stride.Coordinates` | `VL.GIS.Stride/CoordinateConverter.cs` |
| `GIS.Stride.Tessellation` | `VL.GIS.Stride/GeometryTessellator.cs` |
| `GIS.Stride.Elevation` | `VL.GIS.Stride/ElevationNodes.cs` |

### Key Technical Decisions
- **Float precision**: GIS coordinates are `double`; Stride renders as `float`. Use `CoordinateConverter.LonLatToLocal` with a scene origin to avoid float jitter at global scales.
- **Async tiles**: `FetchTileAsync` returns `IObservable<byte[]?>` — connect to S+H in vvvv to latch the result.
- **Immutability**: NTS geometries are immutable after creation; operations return new geometry objects.
- **CRS**: WGS84 (EPSG:4326) is the default for geometry storage. Reproject to Web Mercator (EPSG:3857) for tile alignment or UTM for metric calculations.

## Next Phases

- **Phase 2**: 2D rendering via VL.Skia (tiles as `SKBitmap`, vectors as `SKPath`)
- **Phase 3**: Full 3D Stride integration (tile quads, triangulated polygon meshes, DEM heightmaps)
- **Phase 4**: File I/O via MaxRev.Gdal.Core (GeoTIFF, SHP, GeoJSON, KML)
