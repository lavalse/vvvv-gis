# Why VL.GIS is shaped this way

The other two documents cover mechanics: [VL-PACKAGING.md](VL-PACKAGING.md) is the field guide
to what silently breaks, and [../README.md](../README.md) is the node reference. This one is the
reasoning — what the library is for, what it will never contain, and the principles a new node
should follow. It exists because these questions were settled once, at length, and re-deriving
them costs more than reading them.

---

## Contents

- [A toolbox, not a map engine](#a-toolbox-not-a-map-engine)
- [What this will never contain](#what-this-will-never-contain)
- [Where the substance actually is](#where-the-substance-actually-is)
- [Node design principles](#node-design-principles)
- [Package boundaries](#package-boundaries)
- [Attribution as information](#attribution-as-information)
- [Honesty is a feature](#honesty-is-a-feature)

---

## A toolbox, not a map engine

VL.GIS provides geometry, projection, formats, tile indexing and mesh generation. **What a map
looks like on screen is the patch author's business.**

This is not modesty, it is a reading of how vvvv is used. A vvvv user reaches for a patching
environment precisely because they want to compose the picture themselves; handing them a
finished `MapControl` with a styling API takes away the part they came for. The same instinct
runs through VL.Skia itself — layers are *values* you combine, not a scene you configure — and a
GIS library that fought that would feel foreign in every patch it appeared in.

So the boundary is: **we compute, VL.Skia and VL.Stride draw.** `GIS.Skia.Tiles` says where a
tile belongs; `DrawImage` puts it there. `GeometryToPath` produces an `SKPath`; the patch decides
its colour, stroke and blend mode. No node in this repository has a colour pin, and that is
deliberate.

The practical dividend is that we never have to answer "how do I change the label font", because
labels are drawn by whatever the patch author already uses for text.

## What this will never contain

Recorded as *never*, not *later*, so it stops coming up:

**Our own map engine.** Rendering, styling, labelling, collision, level-of-detail and input
handling is a full-time team's product. Attempting it would also contradict the section above.

**A Cesium-style 3D globe.** There is nothing to wrap: `cesium-native` binds Unity and Unreal
only, with no .NET target, and the JavaScript CesiumJS is not embeddable here in any form worth
having. Writing one from scratch is a larger project than everything else in this repository put
together.

**Anything that wraps Mapsui.** Not because Mapsui is wrong — it is the obvious candidate and was
measured seriously — but because one package should wrap one library, so it would belong in a
`VL.Mapsui` repository. It is separately blocked on a version conflict; see CLAUDE.md for the
measurements.

## Where the substance actually is

**VL.GIS is a thin wrapper, and the honest framing is that almost all the value is upstream.**
NetTopologySuite, ProjNet and BruTile are mature, tested, and used far beyond this project. What
this repository solves is *how they show up as nodes in a patch*, which turned out to be
genuinely hard — nine releases installed cleanly and contributed nothing.

Two consequences worth stating plainly:

**"Wrap everything QGIS can do" is a smaller job than it sounds.** Most vector algorithms are
already in NetTopologySuite; we simply have not exposed them all as nodes. Adding one is usually
a `public static` method and a doc comment. What QGIS really adds beyond that is file formats
(GDAL's job), raster analysis (also GDAL) and a GUI (not our goal).

**Our own arithmetic is the part to be suspicious of.** Viewport maths, tile layout, the local
scene origin and the renderer-space conversion are ours. They have tests, but they do not have
NetTopologySuite's mileage, and a bug there is our bug. Nodes say which case they are in — see
[Attribution as information](#attribution-as-information).

## Node design principles

Each of these came out of something that went wrong or read badly in a patch.

**For the unit of work itself — what earns a node, how many pins it may have, what may be bundled
into one and what must never be — see [NODE-DESIGN.md](NODE-DESIGN.md).** It is the measured
version of this section: 94% of the ecosystem's nodes take three inputs or fewer, help patches
outnumber nodes in every library people learn from, and the answer to "make it easier to
understand" turns out to be more help patches rather than fatter nodes.

### Every opaque value needs a reader node

A record or struct shows up in a patch as a name with no way to see inside. If a node returns
one, something must be able to open it, or the author is holding a value they cannot inspect and
cannot debug. Hence `MapViewInfo`, `CoordinateSystemInfo`, `TileIndexParts`,
`TileDestinationParts` — four instances, which makes it a rule rather than a convenience.

### Return primitives when a type has no representation in a patch

`SKRect` cannot be opened by any node, so `TileDestination` returning one left the caller with
neither the numbers nor a way to use them. `TileDestinationParts` returns four floats, which
connect to anything and can be read. Prefer the form the patch can actually manipulate, even
when the typed form is cleaner C#.

### Prefer a conversion over a setting that fails silently

VL.Skia's space can be switched with an enum on the Renderer, but an unrecognised value is
replaced by the default without a word — so a patch depending on that pin depends on something
whose failure is invisible and looks exactly like a broken patch. `ToRendererSpace` converts the
numbers instead and needs no configuration.

Generalised: **when a setting's wrong value is indistinguishable from a bug, design the thing
that does not need the setting.** Three separate debugging rounds were lost to this one.

### A machine-dependent default is a node, and a path is a `Path`

Not yet needed here, and it will be the moment file I/O arrives — `MaxRev.Gdal.Core`, Shapefile,
GeoTIFF are all on the roadmap, and every one of them takes a path. Established in VL.Mapsui while
adding a cache-folder pin, so it does not have to be rediscovered.

**A default that depends on the machine cannot be a pin's initial value.** A C# default parameter
value must be a compile-time constant, so `Environment.GetFolderPath(...)` is rejected outright:

```
error CS1736: Default parameter value for 'folder' must be a compile-time constant
```

Nor should a literal be substituted: that ships one machine's path inside the node definition, which
is exactly what vvvv's own `VL.Audio.vl` does — its `Filename` pin arrives reading
`C:\temp\foo.wav`. **vvvv's answer is a node that yields the path**: `SystemFolder [IO]` takes a
`VL.Lib.IO.SpecialFolder` and outputs a `VL.Lib.IO.Path`. So leave the pin empty for "use the
default" and add a node that makes the default *discoverable by patching*.

**And a path pin is `VL.Lib.IO.Path`, never `string`.** 54 members of VL.CoreLib take that type, and
its IOBox opens a file chooser on rightclick, a directory chooser on SHIFT+rightclick. A `string`
pin makes the author type the path out by hand. Verified that a C#-imported node accepts it even
though no C# assembly shipped with vvvv 7.4 does this — every precedent is in `.vl`-defined nodes.
Declare it `Path? folder = null`, which is legal because `null` *is* a constant.

One trap, which the Gray Book states outright: *"Path IOBoxes always store relative paths if
possible but actually hide this fact from you!"* A relative path arriving at a node that writes
files cannot be honestly rooted — relative to the document, to vvvv's install folder, to whatever
the working directory happens to be? `Directory.CreateDirectory` silently picks the last. Refuse it
and say so, the same way an unusable folder must never silently fall back to a default.

### Never block on a task inside a node

`FetchTileBytes` in `0.1.0-alpha` awaited an async call and blocked on the result, which
deadlocks whenever the caller owns a `SynchronizationContext` — as vvvv's runtime thread does. It
closed the window without ending the process. Async work returns `IObservable`, or is wrapped in
`Task.Run` so it cannot see the caller's context.

### Immutability, because NetTopologySuite is immutable

Operations return new geometries. Matching upstream avoids a category of surprise where a patch
mutates a value another node still holds.

### Say what the units are, in the node

`Buffer(geom, 0.001)` on WGS84 is a thousandth of a *degree*, roughly 111 m at the equator, not a
millimetre. Units follow the CRS and nothing warns otherwise, so the doc comment has to.
Coordinate order is **(longitude, latitude)** everywhere — x first — which is the most common
source of bugs in this domain and is stated on every node that takes a pair.

## Package boundaries

Four rules, derived by surveying every pack shipped with vvvv 7.4 and all 304 `VL.*` packages on
nuget.org. The counts are in CLAUDE.md; the principles are:

**Declare the upstream nuget, forward only your own assembly.** Every community package that
wraps a third-party library does this — `VL.OpenCV → OpenCvSharp4`, `VL.Assimp → AssimpNet` —
and none sets `IsForward="true"` on anything but its own wrapper. Forwarding someone else's
assembly would make us responsible for how their API looks as nodes.

**Domain names for multi-library packages, library names for single-library ones.** VL.GIS wraps
four libraries, so it sits alongside `VL.Audio` and `VL.2D`, not `VL.NetTopologySuite`. A package
named after a library should wrap exactly that library.

**Renderers go in companion packages.** Four separate families do this (`VL.ImGui`, `VL.CEF`,
`VL.Avalonia`, `VL.Flex`, each with `.Skia` / `.Stride` siblings). The core never depends on a
renderer, which is why `VL.GIS.Skia` exists and why `VL.GIS.Stride` will be its own package too.

**One package per wrapped library.** This is what sends Mapsui to its own repository.

Licence isolation has a precedent (`VL.Audio.GPL` exists to keep GPL code out of `VL.Audio`) but
was judged unnecessary here: ProjNet is LGPL-2.1, reprojection is core GIS rather than an
optional extra, and under the dynamic linking a normal vvvv export produces, §6 asks for nothing
beyond the attribution already in `THIRD-PARTY-NOTICES.md`.

## Attribution as information

Every node's XML doc comment names the library its answer comes from, which vvvv shows as the
tooltip. This started as a licensing courtesy and turned out to be useful for a different reason:
**"this is our own arithmetic" is information the user wants.**

NetTopologySuite's `Buffer` has two decades of production use behind it. `ToRendererSpace` has
seven tests and one afternoon. A patch author debugging an odd result should know which of those
they are looking at, and the tooltip is where they will look.

The corollary is that attribution must be per node and must be **checked**. A blanket
per-file claim asserted several false things — `LonLatToWebMercator`, `WebMercatorToLonLat` and
`UtmZoneFromLongitude` use no ProjNet at all, and the GeoJSON nodes come from
`NetTopologySuite.IO.GeoJSON` rather than the core library. A wrong attribution is worse than
none, because it points the reader at innocent code.

## Honesty is a feature

The README opens with a table splitting the library into **verified / thinly verified / missing**,
naming specific things that have never been run. This is deliberate and should survive future
edits.

The reasoning is that a package which oversells itself costs its users far more than one that
undersells: someone who builds on "tile fetching works" and discovers it was measured once, by
hand, has lost a day and their trust. The same discipline applies internally — there are three
kinds of verification here and they prove different things:

| | proves |
|---|---|
| `dotnet test` | the arithmetic is right |
| `Test-VLPackage.ps1` | the package is structurally capable of contributing nodes |
| `verify.ps1` | vvvv loads, compiles and can consume it |

**Only the GUI proves a node appears under the expected category**, and none of the three proves
a node does something useful. Being precise about which one you have run is not pedantry; the
alternative is [False proofs](VL-PACKAGING.md#false-proofs--verification-that-could-not-have-failed),
which cost this project five separate detours.
