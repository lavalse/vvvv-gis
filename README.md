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

## 🛑 Superseded — and if you installed it, you need to clean up by hand

**`VL.GIS 0.2.0-alpha` is unlisted on nuget.org. Do not install it.** What it did has been split
into three focused packages, each wrapping one library, plus a course that teaches them together:

| instead of VL.GIS | use |
|---|---|
| geometry | [VL.NetTopologySuite](https://github.com/rednotfound/VL.NetTopologySuite) |
| reading and writing GeoJSON | [VL.GeoJSON](https://github.com/rednotfound/VL.GeoJSON) |
| tiles, layers, drawing a map | [VL.Mapsui](https://github.com/rednotfound/VL.Mapsui) |
| learning any of it | [VL.Cartography](https://github.com/rednotfound/VL.Cartography) — installs all three |

**Uninstalling VL.GIS is not enough, and this is the part that catches people.** vvvv keeps every
package's dependencies in one flat, machine-wide folder, and **uninstalling never removes them** —
nor does reinstalling vvvv, because the folder lives in your user profile. Two of VL.GIS's
dependencies actively break VL.Mapsui while they sit there:

```
%LOCALAPPDATA%\vvvv\gamma\nugets\BruTile.6.0.0
%LOCALAPPDATA%\vvvv\gamma\nugets\NetTopologySuite.Features.2.0.0
%LOCALAPPDATA%\vvvv\gamma\nugets\NetTopologySuite.IO.GeoJSON.3.0.0
```

Delete those three, or better, **move them somewhere else first** — that is reversible, and it is
what we did on our own machine.

Why each one hurts:

- **BruTile 6** — `Mapsui.Tiling` requires `BruTile [5.0.6, 6.0.0)`, and `BruTile.Attribution`
  changed layout between 5 and 6. Loading both throws `TypeLoadException`. The flat folder holds
  **one** version of each library and it wins over the copy shipped beside a package's own
  assembly, so whichever landed there first decides for everything vvvv loads.
- **NetTopologySuite.Features 2.0.0** — VL.GIS declares `NetTopologySuite.IO.GeoJSON 3.0.0`, whose
  own nuspec requires `NetTopologySuite.Features [2.0.0, 3.0.0)`. **NuGet resolves the floor of a
  range**, so 2.0.0 was installed. VL.GeoJSON needs 2.1.0 — the two differ only by additions,
  `FeatureExtensions.GetOptionalId` and `IUnique`, which are exactly what reads a GeoJSON `id`.
  With 2.0.0 present you get `TypeLoadException: Could not load type
  'NetTopologySuite.Features.IUnique'`.

  The sharp detail, for anyone writing a nuspec: `NetTopologySuite` itself has the *same* range in
  the same file and resolved to 2.6.0, because something else asked for 2.6.0 **explicitly**.
  Nobody asked for a `NetTopologySuite.Features` version, so it got the floor. **One missing
  explicit declaration, five months of consequences.**

The source in this repository dropped BruTile in commit `15d40f5` — but that fix was never
published, so the package on nuget.org is worse than the code here.

---

## ⚠️ Status: early

**Be careful about using this in a real project yet.**

This is a spare-time project that moves slowly. What has actually been verified is still
narrow, and it is worth being precise about where the edges are:

| | |
|---|---|
| ✅ Verified | The package builds, packs, installs, and its nodes appear in the NodeBrowser under the right categories. |
| ✅ Verified | Geometry, projection, serialization, tile indexing, mesh and viewport arithmetic, by 134 tests run on every push. Four help patches exercise the same paths inside vvvv. |
| ✅ Verified | **A map tile reaches the screen.** `help\VL.GIS.Skia\HowTo Show a map.vl` fetches an OSM tile and draws it in vvvv 7.4 — the whole chain, from lon/lat to pixels. |
| ⚠️ Thinly verified | **Tile fetching over the network** — no automated test crosses the network, and the map patch draws exactly one tile; many at once needs a `ForEach` region that has not been built. Also untested: WKT parsing of exotic CRS, tessellation of concave and holed polygons, everything to do with caching. |
| ❌ Missing | Geometry drawn on the map. `GeometryToPath` exists, has tests, and has never been put on screen. |

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

Two documents sit behind this one: [docs/DESIGN.md](docs/DESIGN.md) explains why the library is
shaped the way it is — what it is for, what it will never contain, and the principles a new node
follows — and [docs/VL-PACKAGING.md](docs/VL-PACKAGING.md) records everything that silently
breaks when packaging for vvvv.

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
| `VL.GIS.Skia` | `GIS.Skia.Viewport` | What is on screen; geographic coordinates to pixels, panning, zooming |
| `VL.GIS.Skia` | `GIS.Skia.Tiles` | Which tiles a view needs, and the `SKRect` each belongs in |
| `VL.GIS.Skia` | `GIS.Skia.Paths` | Geometry to `SKPath`, positioned for a view |

`VL.GIS.Skia` is a **separate package** — install it only if you want to draw. The core has
no dependency on any renderer, which is the convention every other vvvv library follows
(`VL.ImGui` / `.Skia` / `.Stride`, and the same for `VL.CEF` and `VL.Avalonia`). It draws
nothing itself: it hands VL.Skia an `SKImage` and an `SKRect`, or an `SKPath`, and what
those look like is yours to decide.

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
git clone https://github.com/rednotfound/vvvv-gis
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
.\start.ps1                     # build, then open vvvv with VL.GIS loaded
```

That is the whole thing. It rebuilds, offers a list of documents to open, and launches vvvv
against the freshly staged package. Double-clicking `start.cmd` in Explorer does the same.

```
  1  SmokeTest               test\
  2  HowTo Show a map        help\VL.GIS.Skia\
  3  HowTo Buffer in metres  help\VL.GIS\
  4  HowTo Create a point    help\VL.GIS\
  5  HowTo Fetch a map tile  help\VL.GIS\
  6  (blank patch)           (you must add the VL.GIS dependency yourself)

open [1-6], Enter for 1:
```

`.\start.ps1 tile` skips the menu by matching a name fragment. `-NoBuild` skips the build,
`-Restart` ends a vvvv that is still running, `-Editable` loads VL.GIS from source.

**Do not start vvvv from the Start menu while developing.** VL.GIS is never installed into
`%LOCALAPPDATA%\vvvv\gamma\nugets`; it is read off disk from `dist\`, which only happens
with

```powershell
vvvv.exe --package-repositories <repo>\dist
```

Note the nesting — the argument is the *repository* (`dist\`), which contains one folder
per package (`dist\VL.GIS\`). Passing the package folder itself finds nothing, and says
nothing. Neither does launching without the switch at all: the nodes are simply absent.

The other two commands, when you want them separately:

```powershell
.\build.ps1                     # build + stage dist\VL.GIS
.\test\verify.ps1               # headless check (seconds)
```

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

### GIS.Skia.Viewport

`src/VL.GIS.Skia/ViewportNodes.cs` — in the **VL.GIS.Skia** package.

A `MapView` is a centre in WGS84, a slippy-map zoom (fractional is fine) and a size in
pixels. Everything else in `GIS.Skia` is a function of it.

`CreateMapView`, `MapViewInfo`, `Resolution`, `LonLatToScreen`, `ScreenToLonLat`,
`ViewBounds`, `ToRendererSpace`, `PanByPixels`, `ZoomAround`

`ZoomAround` holds one screen pixel still while zooming, which is what a scroll wheel over a
map has to do. `Resolution` is metres per pixel at the view's centre latitude — the number a
scale bar needs.

**`ToRendererSpace` is the one you cannot skip.** Everything above works in pixels from the
top-left of the view; VL.Skia does not. Its default space spans roughly 2.8 × 2 units with the
origin at the centre, so a pixel position of a few hundred handed straight to a layer node lands
far off screen — nothing drawn, no error, nowhere to look. `ToRendererSpace` converts a pixel
rectangle into the units the renderer is actually using, and needs no configuration to do it.

### GIS.Skia.Tiles

`src/VL.GIS.Skia/TileLayoutNodes.cs`

`VisibleTiles`, `TileDestination`, `TileDestinationParts`, `VisibleTileLayout`, `DecodeTile`

`VisibleTileLayout` gives the tile indices and their destination rectangles together, so the
two cannot fall out of step. The full chain: fetch each index with `GIS.Tiles`, decode the bytes
with `DecodeTile`, take the rectangle from `TileDestinationParts`, put it through
`ToRendererSpace`, and draw it with VL.Skia's **`DrawImage`** — setting its `Size Mode` to `Size`
and `Anchor` to `TopLeft`, both of which draw nothing when left at their defaults.

(`ImageLayer` takes an `SKRect` directly and would be the obvious choice, but it is internal to
VL.Skia and the compiler rejects it. Hence `TileDestinationParts`, which returns plain floats.)

### GIS.Skia.Paths

`src/VL.GIS.Skia/GeometryPathNodes.cs`

`GeometryToPath`, `GeometriesToPath`

Points become circles — a point has no extent, so it needs one to be visible. Polygon holes
work because the rings are given opposite winding; `SKPath` fills by the non-zero rule, under
which two contours turning the same way would nest as solid instead.

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
| Now | **A whole map, not one tile.** `VisibleTiles` already returns every index a view needs; drawing them all needs a `ForEach` region in the patch |
| Now | Geometry on top of that map — `GeometryToPath` exists and has never been drawn |
| Next | Publish `VL.GIS.Skia`, which is built and working but unreleased |
| Next | Move GeoJSON to `GeoJSON4STJ`, dropping the Newtonsoft.Json version clash with vvvv's own |
| Later | `VL.GIS.Stride`: tile textures, terrain and extruded buildings on the existing `GIS.Mesh.*` |
| Maybe | File I/O — GeoTIFF, Shapefile, KML — via MaxRev.Gdal.Core |
| Never | Our own map engine, or a Cesium-style globe. [Why](docs/DESIGN.md#what-this-will-never-contain) |

---

## License

MIT — see [LICENSE](LICENSE).

Tile data belongs to its providers. OpenStreetMap data is © OpenStreetMap contributors,
licensed ODbL.
