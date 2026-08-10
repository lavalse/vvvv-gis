# VL.GIS

A community GIS / geospatial library for [vvvv gamma](https://vvvv.org). It wraps mature
.NET GIS libraries — NetTopologySuite, ProjNet, BruTile — as vvvv nodes, so you can work
with coordinates, geometries and map tiles without leaving the patch.

Credit where it is due: this is a thin wrapper. The geometry engine, the projection maths
and the tile handling are all upstream work by the
[NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite),
[ProjNet](https://github.com/NetTopologySuite/ProjNet4GeoAPI) and
[BruTile](https://github.com/BruTile/BruTile) teams. VL.GIS mostly decides how they show
up in a patch. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for their licences —
and note that **exporting a vvvv app redistributes their binaries**, which carries
attribution obligations that installing VL.GIS alone does not.

---

## ⚠️ Status: early

**Be careful about using this in a real project yet.**

This is a spare-time project that moves slowly. What has actually been verified is still
narrow, and it is worth being precise about where the edges are:

| | |
|---|---|
| ✅ Verified | The package builds, packs, installs, and its nodes appear in the NodeBrowser under the right categories. |
| ✅ Verified | Geometry, projection, serialization, tile indexing and mesh arithmetic, by 95 tests run on every push. Three help patches exercise the same paths inside vvvv. |
| ⚠️ Thinly verified | **Tile fetching over the network** — one tile, fetched by hand, no test coverage. Also untested: WKT parsing of exotic CRS, tessellation of concave and holed polygons, everything to do with caching. |
| ❌ Missing | More help patches. There are three, covering points, buffers and map tiles. |

Expect missing edge cases and breaking changes to node names and categories between
releases. If you try it, treat it as a starting point to read and fix rather than something
to build on. Bug reports and PRs are very welcome — that is the fastest way for this to
become trustworthy.

The 0.0.x releases on nuget.org (0.0.1 – 0.0.4) never worked at all: the package installed
and contributed no nodes. They are unlisted. Do not use them.

---

## Contents

- [What's included](#whats-included)
- [Requirements](#requirements)
- [Install](#install)
- [Working on the library](#working-on-the-library)
- [Node reference](#node-reference)
- [Key concepts](#key-concepts)
- [Dependencies and licences](#dependencies-and-licences)
- [Roadmap](#roadmap)

---

## What's included

| Assembly | Node category | Purpose |
|---|---|---|
| `VL.GIS.Core` | `GIS.Geometry` | Create and operate on geometries (points, lines, polygons) |
| `VL.GIS.Core` | `GIS.Projection` | Reproject between coordinate reference systems |
| `VL.GIS.Core` | `GIS.Serialization` | Parse and write WKT, WKB, GeoJSON |
| `VL.GIS.Tiles` | `GIS.Tiles` | Fetch map tiles from OSM and XYZ sources |
| `VL.GIS.Mesh` | `GIS.Mesh.Coordinates` | Convert geographic coordinates to float scene positions without precision loss |
| `VL.GIS.Mesh` | `GIS.Mesh.Tessellation` | Tessellate polygons and lines into triangle meshes |
| `VL.GIS.Mesh` | `GIS.Mesh.Elevation` | Heightmap creation, sampling, normals, terrain mesh |

`VL.GIS.Mesh` produces plain `Vector3` positions and `int` indices. Despite an earlier
name it does **not** depend on Stride — any renderer can consume its output.

---

## Requirements

- **vvvv gamma 7.2 or newer.** The library uses `[Name]` and `[SkipCategory]` from
  `VL.Core`, which arrived in 7.2. Older versions will not place the nodes correctly.
- .NET 8 SDK, only if you want to build from source.

---

## Install

In vvvv: **Manage NuGets** → `nuget install VL.GIS` → restart vvvv.

Then, in a patch, add `VL.GIS` under **Dependencies** (`Ctrl+J` → Solution Explorer).
A package being installed is not the same as your document referencing it — until you add
the dependency, the nodes will not show up in the NodeBrowser.

Double left-click an empty area of the patch to open the NodeBrowser and search for
`CreatePoint`.

---

## Working on the library

```powershell
git clone https://github.com/lavalse/vvvv-gis
cd vvvv-gis
```

Everything below uses the `NuGet.exe` and `vvvvc.exe` that ship with vvvv, so nothing
extra needs installing.

If you are packaging a VL library of your own — or wondering why this one is arranged the
way it is — [docs/VL-PACKAGING.md](docs/VL-PACKAGING.md) is the field guide. Nine releases
of VL.GIS installed and contributed zero nodes before it existed, and none of them produced
an error message; that document is the write-up of why.

### The loop

```powershell
.\build.ps1                     # build + stage dist\VL.GIS
.\test\verify.ps1               # headless check (seconds)
.\test\test.ps1                 # launch vvvv against dist\
```

`build.ps1` stages the package into `dist\VL.GIS\` with exactly the layout it has once
installed, and `dist\` is what you point vvvv at:

```powershell
vvvv.exe --package-repositories <repo>\dist
```

Note the nesting — the argument is the *repository* (`dist\`), which contains one folder
per package (`dist\VL.GIS\`). Passing the package folder itself does not work.

A running vvvv holds the staged assemblies open, so `build.ps1` refuses to run while it is
open. Rebuilding would not update the loaded nodes anyway.

### Writing C# nodes

```powershell
.\test\dev.ps1
```

Opens a scratch document that references the `.csproj` directly rather than the built
`.dll`, so saving a `.cs` file recompiles and hotswaps the running code — no rebuild, no
restart. Static methods hotswap cleanly; stateful instances lose their state on every
save.

For breakpoints, attach Visual Studio to `vvvv.exe` and turn off
*Require source files to exactly match the original version* — hotswapped assemblies no
longer match what is on disk.

Two rules for anything that ships:

- A shipped `.vl` must never contain a `<ProjectDependency>`. It forces the package and
  everything depending on it to stay editable, costing startup time and memory. That is
  why the hot-reload document is separate.
- Every forwarded assembly needs `[assembly: ImportAsIs(Namespace = "VL")]` from
  `VL.Core`. Without it the package loads, compiles, packs and exports with zero warnings
  — and contributes no nodes. `verify.ps1` checks this first for exactly that reason.

### Verification

```powershell
.\test\verify.ps1 -EndToEnd
```

| Stage | What it proves |
|---|---|
| 0 | Every forwarded assembly carries a `VL.Core.Import` attribute |
| 1 | The package document deserializes, resolves its dependencies and compiles |
| 2 | A separate document can consume the packed `.nupkg` (needs gamma ≥ 7.1) |

```powershell
.\tools\Test-VLPackage.ps1      # static checks only, no vvvv needed; this is what CI runs
```

Neither proves a node appears in the NodeBrowser under the expected category — check that
once in the GUI.

### Tests

```powershell
dotnet test VL.GIS.sln          # 95 functional tests, about a second
```

`test\VL.GIS.Tests\` calls the same public statics vvvv turns into nodes, so it covers the
arithmetic — coordinate order, buffer units, UTM zones, projection round trips, tile
indexing, WKT/WKB/GeoJSON, mesh conversion. It says nothing about whether a node *appears*;
that is what `Test-VLPackage.ps1` and `verify.ps1` are for, and the three do not overlap.

No test touches the network. Fetching a tile during a build would be flaky and would breach
the OSM tile usage policy.

### Editing VL.GIS.vl

Don't, by hand. It is generated by `tools\New-VLDocument.ps1`, and three things about it
fail silently if you get them wrong:

- IDs are exactly 22 characters, first in `[A-V]`, rest `[0-9A-Za-z]`, all unique
- the file is UTF-8 **with** BOM
- the `<Patch>` block containing the Application node must be present

To add a dependency, append one line with a fresh ID from `tools\New-VLId.ps1`. Do not
regenerate the file: existing IDs are identities and must stay stable across releases.

Each of those three, plus the `[ImportAsIs]` requirement above, is documented with its
symptom and its forensics in [docs/VL-PACKAGING.md](docs/VL-PACKAGING.md), along with a
checklist to work through when nodes do not show up.

### Releasing

1. Bump `<version>` in `VL.GIS.nuspec`, update `<releaseNotes>`, and keep the version
   `test\SmokeTest.vl` pins in step with it
2. `.\build.ps1 ; .\test\verify.ps1 -EndToEnd`, and check the categories in the GUI
3. Commit, then `git tag v0.2.0-alpha && git push origin v0.2.0-alpha`

The tag triggers `.github/workflows/publish.yml`, which builds, validates, packs and
pushes to nuget.org using the `NUGET_KEY` repository secret. A published version can never
be replaced, which is why validation runs before the push.

---

## Node reference

Every `public static` method on a public class becomes a node. Parameter names become pin
labels (camelCase → "Camel Case"), `out` parameters become extra output pins, and XML doc
comments become tooltips.

### GIS.Geometry

`src/VL.GIS.Core/GeometryNodes.cs` — geometries default to WGS84 (EPSG:4326).
Coordinates are `(longitude, latitude)` — **longitude first**.

**Creation** — `CreatePoint`, `CreatePoint3D`, `CreateLineString`, `CreatePolygon`,
`CreatePolygonWithHoles`, `CreateBoundingBox`

**Operations** — `Buffer`, `BufferWithStyle`, `Intersection`, `Union`, `Difference`,
`SymmetricDifference`, `ConvexHull`, `Centroid`, `Envelope`, `Simplify`

**Predicates** — `Intersects`, `Contains`, `Within`, `Touches`, `Disjoint`, `Covers`

**Measurement** — `Area`, `Length`, `Distance`, `GetGeometries`, `GetCoordinates`

### GIS.Projection

`src/VL.GIS.Core/ProjectionNodes.cs`

| Node | Description |
|---|---|
| `Wgs84` / `WebMercator` | EPSG:4326 and EPSG:3857 coordinate systems |
| `CreateUtm` / `UtmZoneFromLongitude` | UTM zone for metric calculations |
| `ParseWkt` | Parse any CRS from a WKT string |
| `CoordinateSystemInfo` | Name, authority, EPSG code and WKT of a CRS — the only way to confirm in a patch which one you built |
| `CreateTransformation` | Build a reusable transform — cache it, do not rebuild per frame |
| `ReprojectPoint` / `ReprojectPoints` | Reproject raw coordinate pairs; the plural form is much cheaper in bulk |
| `ReprojectPointGeometry` / `ReprojectGeometry` | Reproject geometries; the result carries the target CRS's SRID |
| `LonLatToWebMercator` / `WebMercatorToLonLat` | Direct formulas, no ProjNet involved |

### GIS.Serialization

`src/VL.GIS.Core/SerializationNodes.cs`

| Format | Nodes |
|---|---|
| WKT | `ParseWkt`, `TryParseWkt`, `ToWkt` |
| WKB | `ParseWkb`, `ToWkb`, `ParseHexWkb`, `ToHexWkb` (hex is the PostGIS form) |
| GeoJSON | `ParseGeoJsonGeometry`, `TryParseGeoJsonGeometry`, `ToGeoJsonGeometry` |
| Bounds | `GetBoundingBox`, `BoundingBoxCenter` |

### GIS.Tiles

`src/VL.GIS.Tiles/` — XYZ / slippy-map convention. Zoom 0 is the whole world, 19 is
street level.

**Sources** — `OsmTileSource`, `OpenTopoMapTileSource`, `XyzTileSource`,
`TileSchemaName`, `TileSchemaZoomRange`, `TileAttribution`

There is no WMTS source. Point `XyzTileSource` at a WMTS GetTile URL template instead.

**Indexing** — `CreateTileIndex`, `TileIndexFromLonLat`, `TileIndexParts`,
`TileIndicesForBounds`, `TileBounds`

A `TileIndex` is opaque in a patch — an IOBox shows only its type. Use `TileIndexParts` to
see the column, row and zoom, or `TileBounds` to see the area it covers.

**Fetching** — `FetchTileBytes`, `FetchTileToFile`, `FetchTileAsync`, `FetchTilesAsync`,
`CreateFileCache`, `IsTileCached`

### GIS.Mesh.Coordinates

`src/VL.GIS.Mesh/CoordinateConverter.cs`

`CreateSceneOrigin`, `LonLatToLocal`, `LocalToLonLat`, `CreateWebMercatorOrigin`,
`WebMercatorToLocal`, `MetresPerDegreeLongitude`, `MetresPerDegreeLatitude`

### GIS.Mesh.Tessellation

`src/VL.GIS.Mesh/GeometryTessellator.cs` — convert coordinates with
`GIS.Mesh.Coordinates` *before* tessellating.

`TessellatePolygon`, `TessellateMultiPolygon`, `LineStringToPositions`,
`LineStringToRibbonMesh`, `CreateTileQuad`

### GIS.Mesh.Elevation

`src/VL.GIS.Mesh/ElevationNodes.cs` — heightmaps are flat `float[]` in row-major order.

`CreateFlatHeightmap`, `HeightmapFromArray`, `NormalizeHeightmap`, `SampleHeightmap`,
`GenerateNormals`, `HeightmapToMesh`

---

## Key concepts

**Coordinate order.** Longitude first, then latitude — the same as `(x, y)`. This is the
opposite of how coordinates are usually spoken ("lat, lon"), and it is the single most
common source of confusion.

**Buffer units follow the CRS.** On WGS84 `Buffer(geom, 0.001)` is a thousandth of a
*degree*, roughly 111 m at the equator and less as you move poleward. For anything
metric, reproject to Web Mercator or a UTM zone first, buffer there, and project back.

**Float precision.** WGS84 coordinates carry more meaningful digits than a float32 holds,
so rendering world positions directly produces visible jitter. Pick a scene origin with
`CreateSceneOrigin`, keep it as double, and convert everything to local float offsets.
The equirectangular approximation used is good to about a metre within 50 km of the
origin; beyond that, use several origins.

**Async tiles.** `FetchTileAsync` returns `IObservable<byte[]?>`. Wire it through an
**S+H** node to latch the bytes when they arrive.

**Tile attribution is not optional.** OSM requires
"© [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors" to be displayed.
`TileAttribution` returns whatever the source declares. The
[OSM tile usage policy](https://operations.osmfoundation.org/policies/tiles/) also forbids
bulk downloading; VL.GIS sets an identifying User-Agent, but heavy use needs your own
tile server.

---

## Dependencies and licences

VL.GIS is MIT. It does not redistribute the libraries below — NuGet resolves each as its
own package — but you run their code, so their terms apply to you too.

| Library | Version | Licence | Purpose |
|---|---|---|---|
| [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) | 2.6.0 | BSD-3-Clause | geometry model and spatial operations |
| [NetTopologySuite.IO.GeoJSON](https://github.com/NetTopologySuite/NetTopologySuite.IO.GeoJSON) | 3.0.0 | BSD-3-Clause | GeoJSON read/write |
| [ProjNet](https://github.com/NetTopologySuite/ProjNet4GeoAPI) | 2.1.0 | **LGPL-2.1-or-later** | coordinate reprojection |
| [BruTile](https://github.com/BruTile/BruTile) | 6.0.0 | Apache-2.0 | tile schemas and sources |

ProjNet is the one non-permissive dependency. MIT code with an LGPL dependency is a normal
arrangement, and a normal vvvv export (a folder of loose assemblies) satisfies LGPL-2.1
§6. The obligation only becomes live if you export in a way that statically fuses ProjNet
in — AOT, single-file, ILMerge or aggressive trimming. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). `GIS.Projection` is the only category
that touches ProjNet.

`VL.Core` is a compile-time reference only; it ships with vvvv.

---

## Roadmap

Ordered by what would make the library trustworthy soonest, not by ambition.

| | |
|---|---|
| Next | Help patches — building them is also the first real functional test of the nodes |
| Next | Move GeoJSON to `GeoJSON4STJ`, dropping the Newtonsoft.Json version clash with vvvv's own |
| Later | 2D rendering via VL.Skia: tiles as `SKBitmap`, vectors as `SKPath` |
| Later | Real Stride integration: tile quads and meshes as Stride resources |
| Maybe | File I/O — GeoTIFF, Shapefile, KML — via MaxRev.Gdal.Core |

---

## License

MIT — see [LICENSE](LICENSE).

Tile data belongs to its providers. OpenStreetMap data is © OpenStreetMap contributors,
licensed ODbL.
