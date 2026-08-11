# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repository.

## Project Overview

**VL.GIS** is a community GIS/geospatial library for [vvvv gamma](https://vvvv.org), a
.NET 8 visual programming environment. It wraps mature .NET GIS libraries
(NetTopologySuite, ProjNet, BruTile) as vvvv nodes.

It is a thin wrapper. Almost all the substance is upstream; what this repository actually
solves is **how those libraries show up as nodes in a patch** — which turned out to be the
hard part. See [docs/VL-PACKAGING.md](docs/VL-PACKAGING.md).

**Current state:** `0.2.0-alpha` is on nuget.org. Verified: the package builds, packs and
installs, its nodes appear under the right categories, and 95 tests cover the geometry,
projection, serialization, tile-indexing and mesh arithmetic. Thin: tile fetching over the
network has been done exactly once, by hand. Plenty of nodes have still never run — check
before claiming any particular one works.

`0.1.0-alpha` and `0.0.1`–`0.0.4` are on nuget.org and must never be recommended. The 0.0.x
line contributed no nodes at all; `0.1.0-alpha` deadlocks vvvv when it fetches a tile,
thoroughly enough that the window closes without the process exiting.

## Repository Structure

```
vvvv-gis/
├── VL.GIS.vl              # package entry point — GENERATED, never hand-edit (see below)
├── VL.GIS.nuspec          # NuGet metadata; also the file that gets packed
├── build.ps1              # build + stage dist\VL.GIS\  (the installed-package layout)
├── pack.ps1               # pack into dist\feed\        (a local NuGet feed)
├── src/
│   ├── VL.GIS.Core/       # GIS.Geometry, GIS.Projection, GIS.Serialization, GIS.About
│   ├── VL.GIS.Tiles/      # GIS.Tiles
│   └── VL.GIS.Mesh/       # GIS.Mesh.* — no Stride dependency despite the old name
├── tools/
│   ├── New-VLId.ps1              # generate a valid 22-char VL document ID
│   ├── New-VLDocument.ps1        # generate a .vl entry point
│   ├── Find-Vvvv.ps1             # locate the newest installed gamma (>= 7.2)
│   ├── Test-VLImportAttribute.ps1 # does an assembly carry [ImportAsIs]?
│   └── Test-VLPackage.ps1        # static package validator — what CI runs
├── start.ps1 + start.cmd  # build + open vvvv with the package loaded — the way in
├── test/
│   ├── VL.GIS.Tests/      # xunit functional tests — `dotnet test`, no network
│   ├── verify.ps1         # headless verification, 3 stages
│   ├── dev.ps1 + DevLoop.vl   # C# hot-reload loop (ProjectDependency)
│   └── SmokeTest.vl       # consumer document; pins the VL.GIS version
├── docs/VL-PACKAGING.md   # ⭐ the traps and why they are traps — read before packaging work
└── help/                  # example patches: points, buffers, map tiles
```

### Three kinds of verification, no overlap

| | Proves |
|---|---|
| `dotnet test` | the arithmetic is right |
| `Test-VLPackage.ps1` | the package is structurally capable of contributing nodes |
| `verify.ps1` | vvvv actually loads, compiles and can consume it |

Only the GUI proves a node appears under the expected category. Be precise about which of
these you have shown.

## Commands

Prerequisites: .NET 8 SDK, and **vvvv gamma 7.2 or newer** (7.4 is what is installed).
Everything else — `NuGet.exe`, `vvvvc.exe` — ships inside vvvv; nothing extra to install.

```powershell
.\start.ps1                  # build, pick a document, open vvvv with VL.GIS loaded
.\start.ps1 tile             # same, skipping the menu by name fragment
.\build.ps1                  # build + stage dist\VL.GIS\ only
dotnet test VL.GIS.sln       # 95 functional tests, ~1s, no vvvv and no network
.\test\verify.ps1            # headless check, seconds
.\test\verify.ps1 -EndToEnd  # also packs and consumes the .nupkg like a real user would
.\tools\Test-VLPackage.ps1   # static checks only, no vvvv needed (this is what CI runs)
.\test\dev.ps1               # C# hot-reload loop for writing node code
```

**vvvv must be launched with `--package-repositories dist`** or VL.GIS is simply not
available — it is never installed into `%LOCALAPPDATA%\vvvv\gamma\nugets\`. Launching from
the Start menu will not find the package, and nothing will say so. `start.ps1` exists so
that cannot happen; it also ends a vvvv left running without a window, which otherwise
blocks the next build.

Do **not** use `dotnet pack` — it packs per-project. The package is defined by
`VL.GIS.nuspec` and must be packed with `nuget pack VL.GIS.nuspec` (`pack.ps1` does this).

`build.ps1` refuses to run while vvvv is open: a running vvvv holds the staged assemblies
open, and rebuilding would not update already-loaded nodes anyway.

## Invariants — break any of these and the package fails *silently*

Nine releases (0.0.1–0.0.11) shipped, installed, and contributed zero nodes, with no error
message anywhere. Three independent causes, all listed here. Full forensics in
[docs/VL-PACKAGING.md](docs/VL-PACKAGING.md).

1. **`VL.GIS.vl` is generated, never hand-written.** Every `Id` is exactly 22 characters,
   first in `[A-V]`, rest `[0-9A-Za-z]`, and unique within the document. Use
   `tools\New-VLId.ps1`. To add a dependency, **append one line with a fresh ID** — never
   regenerate the file, because existing IDs are identities that must stay stable across
   releases.
2. **`VL.GIS.vl` is UTF-8 *with* BOM.** Without the BOM vvvv will not load it. Any tool
   that rewrites it must use `New-Object System.Text.UTF8Encoding($true)`.
3. **Every forwarded assembly needs `[assembly: ImportAsIs(Namespace = "VL")]`** from
   `VL.Core` (see each project's `AssemblyInfo.cs`). Without it the package loads,
   compiles, packs and exports with zero warnings — and its methods are demoted to raw
   .NET reflection nodes that the NodeBrowser hides. This is indistinguishable from the
   package not loading at all.
4. **A shipped `.vl` must never contain `<ProjectDependency>`.** It references a `.csproj`
   and forces the package and everything downstream to stay editable. It belongs only in
   `test/DevLoop.vl`.
5. **Never repack a version number that already exists locally.** NuGet treats a version as
   immutable and will serve a stale copy from `~\.nuget\packages\<id>\<version>` forever.
   `pack.ps1` evicts that directory; keep it that way.
6. **nuget.org is not a test environment.** The whole loop is local — `dist\` as a package
   repository, `dist\feed` as a NuGet feed. A published version can never be replaced.

## Architecture

### How a C# method becomes a node

Every `public static` method on a public class becomes a node. camelCase parameter names
become "Camel Case" pin labels, `out` parameters become extra output pins, XML doc
comments become tooltips.

### Node category rule

```
category = (.NET namespace minus the "VL" prefix) + type name
```

The prefix comes from `[assembly: ImportAsIs(Namespace = "VL")]`. Two attributes adjust it:

- `[Name("X")]` — renames the type **for VL only**; the C# class keeps its name.
- `[SkipCategory]` — drops the type level entirely, so members land in the namespace category.

| Category | Source | How |
|---|---|---|
| `GIS.Geometry` | `VL.GIS.Core/GeometryNodes.cs` | namespace `VL.GIS` + `[Name("Geometry")]` |
| `GIS.Projection` | `VL.GIS.Core/ProjectionNodes.cs` | `[Name("Projection")]` |
| `GIS.Serialization` | `VL.GIS.Core/SerializationNodes.cs` | `[Name("Serialization")]` |
| `GIS.About` | `VL.GIS.Core/AboutNodes.cs` | `[Name("About")]` |
| `GIS.Tiles` | `VL.GIS.Tiles/*.cs` | namespace `VL.GIS.Tiles` + `[SkipCategory]` |
| `GIS.Mesh.Coordinates` | `VL.GIS.Mesh/CoordinateConverter.cs` | `[Name("Coordinates")]` |
| `GIS.Mesh.Tessellation` | `VL.GIS.Mesh/GeometryTessellator.cs` | `[Name("Tessellation")]` |
| `GIS.Mesh.Elevation` | `VL.GIS.Mesh/ElevationNodes.cs` | `[Name("Elevation")]` |

Note `RootNamespace` is `VL.GIS` in every project, deliberately decoupled from the
assembly name — otherwise `VL.GIS.Core` would produce `GIS.Core.Geometry`.

`[Name]` and `[SkipCategory]` arrived in gamma **7.2**, which is what sets the minimum
vvvv version. `VL.Core` is pinned to `2025.7.2` for the same reason and is deliberately
absent from the nuspec — it ships with vvvv.

### Key technical decisions

- **Coordinate order is (longitude, latitude)** — x first. The most common source of bugs.
- **Buffer units follow the CRS.** `Buffer(geom, 0.001)` on WGS84 is a thousandth of a
  *degree* (~111 m at the equator), not a millimetre. Reproject before metric work.
- **Float precision.** WGS84 doubles do not survive float32. `CreateSceneOrigin` +
  `LonLatToLocal` keeps rendering positions small; good to ~1 m within 50 km of the origin.
- **Async tiles.** `FetchTileAsync` returns `IObservable<byte[]?>` — latch it with S+H.
- **Immutability.** NTS geometries are immutable; operations return new objects.

## Package boundaries

Settled by surveying every pack shipped with vvvv 7.4, the community packages installed
here, and all 304 `VL.*` packages on nuget.org. Recorded so it does not have to be
re-derived.

**Declare the upstream nuget, forward only your own assembly.** Every community package
that wraps a third-party library declares it as a `<NugetDependency>` in its `.vl` —
`VL.OpenCV → OpenCvSharp4`, `VL.Assimp → AssimpNet`, `VL.PolyTools → com.angusj.Clipper`,
`VL.SimpleHTTP → RestSharp`, `VL.IO.Midi → managed-midi` — and none of them sets
`IsForward="true"` on anything but its own wrapper. VL.GIS matches this already.

**Domain names for multi-library packages, library names for single-library ones.** The
common second-level prefixes are `IO` (35), `Devices` (33), `Stride` (15), `Addons` (9),
`Audio` (7), `2D` (5). Packages named after a library (`VL.OpenCV`, `VL.Rhino.3dm`) wrap
exactly one. VL.GIS wraps four, so the domain name is right — it sits alongside `VL.Audio`
and `VL.2D`, not `VL.NetTopologySuite`.

**Renderers go in companion packages.** Four separate families do this: `VL.ImGui` /
`.Skia` / `.Stride`, `VL.CEF` / `.Skia` / `.Stride`, `VL.Avalonia` / `.Skia` / `.Stride`,
`VL.Flex` / `.Skia` / `.ImGui`. The core never depends on a renderer. So 2D and 3D work
belongs in `VL.GIS.Skia` and `VL.GIS.Stride`, not in this package.

**One package per wrapped library.** Anything that wraps Mapsui belongs in its own
repository (`VL.Mapsui`), not folded in here.

**Licence isolation has a precedent but is not required.** `VL.Audio.GPL` exists purely to
keep GPL code out of `VL.Audio`. VL.GIS deliberately does *not* split ProjNet out despite
its LGPL: reprojection is core GIS functionality rather than an optional extra, and under
dynamic linking — what a normal vvvv export produces — LGPL-2.1 §6 imposes nothing beyond
the attribution already in `THIRD-PARTY-NOTICES.md`.

**Attribution is per node.** Each node's XML doc comment names its upstream library, which
vvvv shows as the tooltip. Nodes that are our own arithmetic say so — that is information
too, since our code lacks the mileage NTS has.

## Mapsui compatibility, measured

Wrapping Mapsui for 2D map rendering is viable. Verified on 2026-08-10 with a throwaway
console project, not inferred:

| | |
|---|---|
| Mapsui 4.1.9 (last of 4.x) | renders a correct, labelled OSM map of Tokyo to PNG, no UI involved |
| the same, running against vvvv's SkiaSharp 2.88.8 | still renders — swap confirmed by file version and native-library size |
| Mapsui 5.x | **unusable**: needs SkiaSharp >= 3.119.2, vvvv ships 2.88.8 |

The 2.88.8/2.88.9 gap turns out not to matter because SkiaSharp keeps assembly version
`2.88.0.0` across the whole 2.88.x line, so the CLR sees one identity. The probe was
negative-tested first — pinning SkiaSharp 2.80.2 fails the build with NU1605 — because a
compatibility check that has never rejected anything proves nothing.

Still unresolved before `VL.Mapsui` can ship: Mapsui 4.1.9 resolves **BruTile 5.0.6** while
VL.GIS uses **6.0.0**, and vvvv loads one assembly set for all packages. Whether the two can
coexist in one vvvv has not been tested.

## Release process

1. Bump `<version>` and `<releaseNotes>` in `VL.GIS.nuspec`, and keep the version pinned in
   `test\SmokeTest.vl` in step.
2. `.\build.ps1 ; .\test\verify.ps1 -EndToEnd`, then confirm the categories in the GUI.
3. Commit and push `main`.
4. **The tag push is the user's to make**, not Claude's. `git tag vX.Y.Z && git push origin
   vX.Y.Z` triggers `.github/workflows/publish.yml`, which packs (the tag, not the nuspec,
   is the version at release time) and pushes using the `NUGET_KEY` repository secret.

Prerelease suffix is `-alpha`. That is the ecosystem norm — of 304 `VL.*` packages on
nuget.org, 81 are prerelease and 52 of those use `-alpha` against 9 using `-pre`. NuGet
treats every `-suffix` identically; only the ordering differs
(`alpha` < `beta` < `pre` < `rc` < release). Drop it once the nodes have been exercised.

## Working style for this repository

- **Change one variable at a time.** The nine failed releases all came from editing the
  `.vl`, the csproj and the nuspec in the same round and having no idea which mattered.
- **Verify locally before claiming anything works.** `verify.ps1` proves the package loads;
  only the GUI proves a node appears; nothing yet proves a node computes the right answer.
- Be precise about which of those three you have actually shown.

## Roadmap

VL.GIS is a **toolbox, not a map engine**: geometry, projection, formats, tile indexing,
mesh. What it looks like on screen is the patch author's business, which is also how vvvv
users prefer to work.

| | |
|---|---|
| Next | See a map. Either `VL.Mapsui` (a separate repo, now known to be feasible) or `VL.GIS.Skia` as a thin bridge — tiles to positioned images, geometry to `SKPath` or SVG path data, both drawn by VL.Skia's existing nodes |
| Next | GeoJSON → `GeoJSON4STJ 4.0.0`, dropping the Newtonsoft.Json clash with vvvv's 13.0.3 |
| Later | File I/O via `MaxRev.Gdal.Core` — Shapefile, GeoTIFF. Without it users can only type coordinates by hand |
| Later | `VL.GIS.Stride`: tile textures, terrain and extruded buildings, built on the existing `GIS.Mesh.*` |
| Never | Our own map engine, or a Cesium-style 3D globe. There is no Cesium to wrap on .NET (`cesium-native` binds only Unity and Unreal), and writing one is a full-time team's project |

On "wrap everything QGIS can do": **most vector algorithms are already in NetTopologySuite**
— we simply have not exposed them all as nodes. What QGIS really adds beyond that is file
formats, raster analysis and a GUI; the first two are GDAL's job and the third is not our
goal. The work is smaller than it sounds.
