# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repository.

## ⚠️ Read this first: VL.GIS is retired

**Do not add nodes here.** The one-package shape is the thing this repository disproved: geometry,
projections, formats, tiles, drawing and terrain are each somebody's whole library, and wrapping
them together produced something shallow in six directions at once. The work was split, one package
per wrapped library, and lives in:

| repository | what it is |
|---|---|
| `D:\2026_Projects\vl-nettopologysuite` | geometry |
| `D:\2026_Projects\vl-geojson` | reading and writing GeoJSON |
| `D:\2026_Projects\vl-mapsui` | drawing maps — wraps Mapsui, a real engine |
| `D:\2026_Projects\vl-cartography` | the course; no nodes, declares the other three |

**Two reasons this repository is still here**, and both constrain what may be done to it:

1. **`GIS.Projection` (ProjNet) and `GIS.Mesh` have no successor.** Arbitrary CRS transformation
   and 3D terrain exist nowhere else in the family. Splitting them out is real future work — and
   note that `Projection` is a bad package name in vvvv, where the word means a projector, so
   `VL.ProjNet`.
2. **`docs/` is where the family's practices were first written down.** `DESIGN.md`,
   `NODE-DESIGN.md`, `VL-PACKAGING.md`, `VL-RUNTIME.md` — the sibling repositories still cite them,
   and `vl-mapsui\docs\RULES.md` opens by saying its rules were carried over from here.

`VL.GIS 0.2.0-alpha` on nuget.org is unlisted. It declares BruTile 6, which breaks VL.Mapsui with
`TypeLoadException`; `README.md` carries the manual cleanup a user needs, because uninstalling does
not remove it.

**Everything below describes the retired package.** It is accurate about the code that is still
here; it is not advice about what to build next.

## Project Overview

**VL.GIS** is a community GIS/geospatial library for [vvvv gamma](https://vvvv.org), a
.NET 8 visual programming environment. It wraps mature .NET GIS libraries
(NetTopologySuite, ProjNet, BruTile) as vvvv nodes.

It is a thin wrapper. Almost all the substance is upstream; what this repository actually
solves is **how those libraries show up as nodes in a patch** — which turned out to be the
hard part. See [docs/VL-PACKAGING.md](docs/VL-PACKAGING.md).

**Current state:** `0.2.0-alpha` is on nuget.org. Verified: the package builds, packs and
installs, its nodes appear under the right categories, and 134 tests cover the geometry,
projection, serialization, tile-indexing, mesh and viewport arithmetic. `VL.GIS.Skia` draws a
real OSM tile on screen — the first end-to-end run of the whole chain — but is unpublished.
Thin: one tile, not many, and no automated test crosses the network. Plenty of nodes have
still never run — check
before claiming any particular one works.

`0.1.0-alpha` and `0.0.1`–`0.0.4` are on nuget.org and must never be recommended. The 0.0.x
line contributed no nodes at all; `0.1.0-alpha` deadlocks vvvv when it fetches a tile,
thoroughly enough that the window closes without the process exiting.

## Repository Structure

**A package is a `.vl` at the repo root with a `.nuspec` of the same name beside it.**
`build.ps1`, `pack.ps1`, `Test-VLPackage.ps1` and both workflows all discover them that way,
so adding one needs no edit to any of them.

```
vvvv-gis/
├── VL.GIS.vl / .nuspec        # package 1 — .vl is GENERATED, never hand-edit (see below)
├── VL.GIS.Skia.vl / .nuspec   # package 2 — depends on VL.GIS
├── build.ps1              # build + stage every package under dist\
├── pack.ps1               # pack every package into dist\feed\  (a local NuGet feed)
├── src/
│   ├── VL.GIS.Core/       # GIS.Geometry, GIS.Projection, GIS.Serialization, GIS.About
│   ├── VL.GIS.Tiles/      # GIS.Tiles
│   ├── VL.GIS.Mesh/       # GIS.Mesh.* — no Stride dependency despite the old name
│   └── VL.GIS.Skia/       # GIS.Skia.* — viewport, tile layout, geometry to SKPath
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
├── docs/VL-PACKAGING.md   # ⭐ getting a node to EXIST — read before packaging work
├── docs/VL-RUNTIME.md     # ⭐ what happens once it RUNS — read before writing any node that
│                          #   touches the network, a file, or any other resource
├── docs/DESIGN.md         # why the library is shaped this way — read before adding a node
│                          #   or answering "should VL.GIS also do X?"
├── docs/NODE-DESIGN.md    # ⭐ what earns a node, how many pins, what may be bundled — read
│                          #   before adding a node, and before "let's make one node that does it all"
└── help/<PackageName>/    # example patches, one folder per package (see below)
```

**Help patches live in `help\<PackageName>\` and are staged as `help\`.** `build.ps1` and each
nuspec agree on that, and `start.ps1` scans it recursively. The split matters: `HowTo Show a
map.vl` needs `VL.GIS.Skia`, and shipping it inside `VL.GIS` gave anyone who installed VL.GIS
alone a patch that opens with a missing dependency. A patch belongs to the *last* package it
depends on, not the first.

### Three kinds of verification, no overlap

| | Proves |
|---|---|
| `dotnet test` | the arithmetic is right |
| `Test-VLPackage.ps1` | every package is structurally capable of contributing nodes |
| `verify.ps1` | vvvv actually loads, compiles and can consume them |

Only the GUI proves a node appears under the expected category. Be precise about which of
these you have shown.

**A package that depends on another package in this repository cannot be compiled by
`vvvvc` in stage 1.** Resolving the dependency needs `--package-repositories dist`, but
that directory also contains the document being compiled, and vvvvc then treats it as a
package rather than as something to build: *"Entry point for document X.vl not found"*.
Confirmed against VL.GIS itself, so it is the flag and not the document. Those packages are
deferred to stage 2, where `test\SmokeTest.vl` consumes them from the packed feed — which is
the real usage anyway, and stronger evidence.

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
7. **An upstream library must be a *package in a package repository*, not just a
   `<NugetDependency>` line.** Declaring it is necessary and not sufficient: without the package
   present, VL cannot resolve its types, so a node whose signature mentions `IHttpTileSource` is
   constructed with no working pins and every link to it is dropped — `vvvvc` exits 0 and nothing
   is red. `build.ps1` installs them into `deps\`, which is kept apart from `dist\` because
   pointing `--package-repositories` at the document being compiled fails with *"Entry point for
   document X.vl not found"*. This repository resolved through vvvv's shared
   `%LOCALAPPDATA%\vvvv\gamma\nugets\` for months without knowing it.

### Runtime invariants — break these and something *outside* the package fails

Numbered alongside the packaging rules deliberately. When work moved to a second repository,
the numbered packaging rules were carried over correctly and these were not, because they had
only ever been written as prose about one particular help patch. **A rule transfers; a note about a file does
not.** Full forensics in [docs/VL-RUNTIME.md](docs/VL-RUNTIME.md).

8. **A `public static` method is evaluated on every frame.** Sixty times a second, from the
   moment the document is opened — opening a `.vl` *is* running it. Anything that acquires a
   connection, file handle, GPU resource, cache, thread or subscription must therefore be a
   `[ProcessNode]` class instead, built once and rebuilt only when an input actually changes.
   Written as a static method, a map node opened **17,000 TCP connections in 13 minutes**,
   exhausted the machine's 16,384 ephemeral ports and took down a home network.
9. **Never block on a task inside a node.** vvvv's runtime thread owns a
   `SynchronizationContext`, so `.Result` / `.Wait()` deadlocks it — the window closes without
   the process exiting. Return `IObservable`, or wrap in `Task.Run`. This shipped in
   `0.1.0-alpha`.
10. **A node pointing at free public infrastructure is off by default.** Zero requests on open,
   a disk cache, a User-Agent naming the package, and an on-screen counter of what has been
   allocated. OpenStreetMap's tile policy forbids bulk downloading, and whoever opens a patch
   has not agreed to anything yet.
11. **Never leave vvvv running unattended, and never start it in the background.** Read the
    value, close it. Leaks accumulate across sessions.

## Architecture

### How a C# method becomes a node

Every `public static` method on a public class becomes a node. camelCase parameter names
become "Camel Case" pin labels, `out` parameters become extra output pins, XML doc
comments become tooltips.

Two things about pins that are settled facts rather than preferences, and will bite as soon as file
I/O lands (`MaxRev.Gdal.Core` is on the roadmap and every node it brings takes a path). Full
reasoning in [docs/DESIGN.md](docs/DESIGN.md); established in VL.Mapsui, recorded here because
**a rule transfers**:

- **A path pin is `VL.Lib.IO.Path`, never `string`.** Its IOBox opens a file chooser on rightclick
  and a directory chooser on SHIFT+rightclick; a `string` pin makes the author type the path out.
  Beware that a Path IOBox stores a *relative* path whenever it can and hides that from you, so a
  node that writes files must refuse a non-rooted path rather than guess what it is relative to.
- **A machine-dependent default cannot be a pin's initial value** — a C# default must be a
  compile-time constant (`CS1736`) — and a hardcoded literal would ship one machine's path inside
  the node, as `VL.Audio.vl`'s `Filename` pin does. Declare it `Path? x = null`, resolve empty
  internally, and expose the default through a node the way `SystemFolder [IO]` does.

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
re-derived. The counts are here; the reasoning behind them, and the rest of the library's
design philosophy, is in [docs/DESIGN.md](docs/DESIGN.md).

**Declare the upstream nuget, forward only your own assembly.** Every community package
that wraps a third-party library declares it as a `<NugetDependency>` in its `.vl` —
`VL.OpenCV → OpenCvSharp4`, `VL.Assimp → AssimpNet`, `VL.PolyTools → com.angusj.Clipper`,
`VL.SimpleHTTP → RestSharp`, `VL.IO.Midi → managed-midi` — and VL.GIS matches this already.

**Corrected 2026-08-13:** this used to add "and none of them sets `IsForward="true"` on anything
but its own wrapper". `VL.Rhino.3dm` does exactly that:
`<NugetDependency Location="Rhino3dm" IsForward="true" Version="7.14.0" />`, to expose that
library's own members as nodes. The survey behind the wrong claim only examined
`PlatformDependency`. VL.GIS does not need it — a package merely being *resolvable* is enough for
its types to appear on pins, see invariant 7 — but the claim as written was false.

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

## Mapsui: blocked, and exactly why

Measured on 2026-08-10 with a throwaway console project. **Folding Mapsui into VL.GIS is not
possible**, and it is worth recording precisely what blocks it, because the blockage is narrow
and will lift on its own.

> **Corrected 2026-08-13.** This section used to say "wrapping Mapsui is not currently
> possible", full stop. That was the right answer to the wrong question. The BruTile constraint
> lives only in `Mapsui.Tiling`, so a *separate* package that never sits beside VL.GIS is
> unaffected — and `D:\2026_Projects\vl-mapsui` (`VL.Mapsui`) now does exactly that, using
> Mapsui 4.1.9 against vvvv's own SkiaSharp. The verdict below is about **coexistence with this
> package**, not about Mapsui.
>
> A verdict is only as good as the premise it was measured under. Write the premise down next
> to the result, or the result outlives it.

Each Mapsui line is individually fine and collectively unusable *inside VL.GIS*:

| | SkiaSharp | BruTile | verdict |
|---|---|---|---|
| Mapsui 4.1.9 | 2.88.9 — works against vvvv's 2.88.8 ✅ | `>= 5.0.6 && < 6.0.0` ❌ | conflicts with VL.GIS |
| Mapsui 5.1.0 | `>= 3.119.2` ❌ vvvv ships 2.88.8 | `>= 6.0.0 && < 7.0.0` ✅ | conflicts with vvvv |

4.1.9 does render — a correct, labelled OSM map of Tokyo to PNG with no UI — and keeps
rendering when run against vvvv's SkiaSharp, because SkiaSharp holds assembly version
`2.88.0.0` across all of 2.88.x so the CLR sees one identity. Forcing BruTile 6.0.0 into that
same probe fails at runtime:

```
System.TypeLoadException: Could not load type 'BruTile.Attribution' from assembly
'Mapsui.Tiling, Version=4.1.9.0' due to value type mismatch.
```

`Attribution` is a struct in both versions, so its field layout changed — an ABI break
nothing can paper over. `IHttpTileSource` also does not exist at all in BruTile 5, and
VL.GIS's whole tile API returns it, so downgrading is a redesign rather than a version bump.

**Mapsui 5.x otherwise matches VL.GIS exactly** — BruTile 6.0.0, NetTopologySuite 2.6.0,
GeoJSON4STJ 4.0. The single obstacle is SkiaSharp, and vvvv is two majors behind (SkiaSharp
reached a stable 4.x in June 2026, co-maintained by Microsoft and Uno). There is no public
signal about when vvvv will move, so **nothing in VL.GIS may depend on Mapsui**. Revisit the
moment vvvv ships SkiaSharp 3 or newer; at that point VL.Mapsui can move to 5.x, the BruTile
conflict disappears, and the two packages become installable side by side.

Both probes were negative-tested before being believed: pinning SkiaSharp 2.80.2 fails the
build with NU1605, so the check can in fact reject something.

### The conflict is machine-wide, not per-project

Measured 2026-08-13, and worth knowing before touching either package. `%LOCALAPPDATA%\vvvv\
gamma\nugets\` is a **flat folder with one version of each library**, shared by everything vvvv
loads, and whatever it finds there wins over a copy sitting next to your own assembly.

Installing VL.GIS from nuget.org therefore put `BruTile.6.0.0` in it — along with
`BruTile.MbTiles.6.0.0` and an SQLite stack, because the very first VL.GIS build declared
MbTiles before `c75f12f` removed it. **Uninstalling a package does not remove its
dependencies**, so all of that outlived VL.GIS's own removal by five months and was still there
with nothing referencing it.

That orphaned BruTile 6 is what made VL.Mapsui throw `TypeLoadException` inside the vvvv editor
even though its own output folder held the correct BruTile 5.0.6 — and note the shape of it: a
`vvvvc` export of the same patch bundles 5.0.6 and runs fine, so only the editor is affected.
Both were moved to `_nugets-backup-VL.GIS\`.

If VL.GIS is ever installed from nuget.org again, BruTile 6 comes back and VL.Mapsui breaks
again. Everyday VL.GIS work goes through `start.ps1` with `--package-repositories dist`, which
never touches that folder, so this only bites after a NodeBrowser install.

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

## A second repository is developed alongside this one

`D:\2026_Projects\vl-mapsui` (`VL.Mapsui`) wraps the Mapsui map engine and is a separate package
for the reason recorded above. Three consequences that have already caused mistakes:

1. **Only this repository's `CLAUDE.md` loads automatically.** `vl-mapsui\CLAUDE.md` has its own
   rules — read it before touching that repository. This is the same mechanism as "a rule
   transfers, a note about a file does not", one level up.
2. **Memory is keyed to the project directory.** Sessions started in `vl-mapsui` get an empty
   memory index and see none of what was recorded here. Start sessions for both from this
   directory, or accept that the two halves of the memory never meet.
3. **The two packages cannot be loaded into vvvv at the same time** (BruTile 5 vs 6). GUI
   verification for them is therefore always two separate vvvv sessions, never one.

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
| Now | One tile renders. Next is many, which needs a `ForEach` region in the patch, then publishing `VL.GIS.Skia` |
| Next | Geometry on the map — `GeometryToPath` exists and has never been drawn |
| Next | GeoJSON → `GeoJSON4STJ 4.0.0`, dropping the Newtonsoft.Json clash with vvvv's 13.0.3 |
| Later | File I/O via `MaxRev.Gdal.Core` — Shapefile, GeoTIFF. Without it users can only type coordinates by hand |
| Later | `VL.GIS.Stride`: tile textures, terrain and extruded buildings, built on the existing `GIS.Mesh.*` |
| Never | Our own map engine, or a Cesium-style 3D globe. There is no Cesium to wrap on .NET (`cesium-native` binds only Unity and Unreal), and writing one is a full-time team's project |

On "wrap everything QGIS can do": **most vector algorithms are already in NetTopologySuite**
— we simply have not exposed them all as nodes. What QGIS really adds beyond that is file
formats, raster analysis and a GUI; the first two are GDAL's job and the third is not our
goal. The work is smaller than it sounds.
