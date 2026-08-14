# What deserves to be a node

[DESIGN.md](DESIGN.md) says what this library is *for*. This one is about the unit of work: what
earns a node, how big one should be, what may be bundled into one, and what must never be. It
exists because until now every node's shape was inherited from whatever the C# method happened to
look like — which is not a design, it is a default — and because the next thing on the roadmap
(file I/O through GDAL: Shapefile, GeoTIFF, GeoPackage) forces the question immediately. One node
per format, or one node that takes a path?

**Every rule here is followed by the measurement or the quotation it came from.** Where a rule
contradicts what the ecosystem actually ships, both are recorded. Surveyed on 2026-08-13: the 45
packs shipped with vvvv 7.4, the 17 community packages installed on this machine, the Gray Book's
own [Design Guidelines](https://thegraybook.vvvv.org/reference/extending/design-guidelines.html)
and [Providing Help](https://thegraybook.vvvv.org/reference/extending/providing-help.html), and
Heron, the GIS plugin for Grasshopper — the closest thing to a peer this library has.

---

## Contents

- [Does this deserve a node](#does-this-deserve-a-node)
- [How big a node should be](#how-big-a-node-should-be)
- [What may be bundled](#what-may-be-bundled)
- [What must never be bundled](#what-must-never-be-bundled)
- [Naming](#naming)
- [Being found](#being-found)
- [Help is the teaching surface, not a fatter node](#help-is-the-teaching-surface-not-a-fatter-node)
- [Two library architectures, and which one this is](#two-library-architectures-and-which-one-this-is)
- [Conventions worth obeying because the ecosystem already does](#conventions-worth-obeying-because-the-ecosystem-already-does)
- [The gap between this document and the current code](#the-gap-between-this-document-and-the-current-code)

---

## Does this deserve a node

Three questions, in order. Any one of them can end it.

**1. Can a patch author reach the same result by wiring three existing nodes?** If yes, it is not a
node — it is a help patch. This is the cheapest test and it kills the most candidates. A node that
exists to save two links costs a name in the NodeBrowser, a tooltip, a test, a version to keep
stable, and a thing to explain; the links cost nothing.

**2. Does it hold a resource — a connection, a file handle, a cache, a thread, a subscription?**
Then it is a `[ProcessNode]`, never a `public static` method, and the question of whether it
deserves to exist is already settled by the resource. See [VL-RUNTIME.md](VL-RUNTIME.md); a static
method is evaluated sixty times a second and this rule was learned by taking a home network down.

**3. Is the thing it decides something the user cares about?** This is the one that decides
granularity, and it cuts both ways:

- If the user *does* care — what the mouse means, where the view looks, where files go — it must
  be reachable, as a node or a pin. Deciding it for them is taking their patch away.
- If the user *does not* care — which decoder reads a Shapefile, which HTTP client fetches a tile —
  it belongs inside, and exposing it just makes them choose something they have no basis to choose.

---

## How big a node should be

The ecosystem's answer is unambiguous. Of the **901** C# static nodes in VL.CoreLib:

| inputs | nodes |
|---|---|
| 1 | 493 |
| 2 | 117 |
| 3 | 40 |
| 4 | 7 |
| 5+ | 25 |

**94% have three inputs or fewer.** And every node with more than five is a machine-generated arity
family — `TypeSwitch8` … `TypeSwitch13`, `Try4`, `Try5` — so **not one designed node in VL.CoreLib
has more than five inputs**.

So:

- **Three inputs is the target.**
- **Four wants a reason** you could say out loud.
- **Five or more means two decisions are wearing one node.** Split it, or move one decision to a
  node upstream that produces a value the first one takes.

VL.GIS today has 17% of its nodes above three inputs, against the ecosystem's 6%. That is a to-do
list, not a style: see [the gap](#the-gap-between-this-document-and-the-current-code).

---

## What may be bundled

**Heron** — the GIS plugin for Grasshopper, GDAL-backed, the nearest peer this library has —
covers the whole GIS domain in **37 components across 5 categories**. Two of its choices are worth
copying outright:

- **`Import Vector` reads SHP, GeoJSON, OSM, KML, MVT, GDB and HTTP sources in one component.** The
  file format is not a decision the user wants to make; they have a file. One node per format would
  also mean a new node for every format GDAL adds.
- **`Slippy Raster` picks the tile service on a pin**, not by having one node per provider.

The test: **bundle a choice the user does not want to make; never bundle a concept they have to
understand anyway.** Format and transport are the first kind. Coordinate reference systems are the
second — a node that silently guessed the CRS would be hiding the single thing GIS is about.

One of Heron's decisions is deliberately *not* copied. Most of its components depend on Rhino's
global `EarthAnchorPoint`, set by a separate component elsewhere in the document. vvvv has no
equivalent global, and hidden global state is exactly what patchers hate — an identical patch
behaves differently depending on something off-screen. VL.GIS passes an explicit origin value
(`CreateSceneOrigin` → `LonLatToLocal`) and should keep doing so, even though it costs a link.

---

## What must never be bundled

Four, each of which this project has already paid for:

| Never decide for the user | What it cost |
|---|---|
| **What the mouse means** | An all-in-one map node handled drag and wheel internally, which silently decided that left-drag pans and the wheel zooms, and left no way to drive the map from an LFO, OSC or a timeline. Composing that is the reason to reach for a patching environment at all. |
| **Where the view looks** | Rebuilding the map when the centre changed turned dragging into a per-frame rebuild. Where it looks goes through the navigator; only what it *is* may rebuild it. |
| **Where files go** | The tile cache wrote to `%LOCALAPPDATA%` with no way to say otherwise. Now `Cache Folder` is a pin, and an unusable path is reported rather than quietly replaced. |
| **Which renderer draws it** | Renderers live in companion packages (`VL.GIS.Skia`, later `.Stride`). Four separate families in the ecosystem do this — ImGui, CEF, Avalonia, Flex — and the core never depends on a renderer. |

The pattern behind all four: **bundling a mechanism is fine, bundling a policy is not.**

---

## Naming

From the Gray Book's Design Guidelines, quoted:

- "for process nodes use nouns: Sequencer, FlipFlop, Copier"
- "for operation nodes prefer verbs: Map, Copy, Sample"
- "avoid node names starting in 'As..' like 'AsString'. Use 'To..' or 'From..' instead"
- "Prefer existing Categories over inventing your own" and "Avoid excessive use of Subcategories"
- `Create` is kept "for complex datatypes … that are more than containers for a bunch of
  properties in that they have some functionality"
- For a datatype that *is* "a container for a bunch of properties, it is often useful to have a
  pair of join/split nodes"

That last one matters more than it looks. [DESIGN.md](DESIGN.md) already says every opaque value
needs a reader node — and the ecosystem already has a name for that reader: **`Split`**. It appears
**194 times** in the help patches shipped with vvvv. A user looking to open a value types `Split`
into the NodeBrowser; `TileIndexParts` is a word only we use.

---

## Being found

The NodeBrowser searches names, categories and **tags**. VL.GIS and VL.Mapsui currently have
**zero** tags; the packs shipped with vvvv use them **416** times.

The reason is structural, and worth knowing before trying to fix it: **`VL.Core.Import` has no
`TagsAttribute`.** The importable attributes are exactly `Name`, `SkipCategory`, `ProcessNode`,
`ProcessNodeFactory`, `Fragment`, `ImportAsIs`, `ImportNamespace`, `ImportType`, `IncludeForeign`.
Tags are a property of a node *definition in a `.vl` document*, not of a C# method.

But a definition does not have to be a hand-patched wrapper. `Tags=` and `Summary=` sit on the
`<Node Name="…">` header **whatever its Kind is, including `ForwardDefinition`** — vvvv's own
`VL.Skia.vl` does exactly this twice:

```xml
<Node Name="Console" Bounds="370,278" Id="ASpy8KSKiqGM5ucBYFuBTZ" Tags="mouse,keyboard">
  <p:NodeReference>
    <Choice Kind="ForwardDefinition" Name="Forward" />
    <FullNameCategoryReference ID="Primitive" />
  </p:NodeReference>
  <p:TypeAnnotation LastDependency="VL.Skia.dll">
    <Choice Kind="TypeFlag" Name="Console" />
    <FullNameCategoryReference ID="VL.Skia" />
  </p:TypeAnnotation>
  <p:ForwardAllNodesOfTypeDefinition p:Type="Boolean">false</p:ForwardAllNodesOfTypeDefinition>
  …
```

**Note the danger in the last line.** `ForwardAllNodesOfTypeDefinition = false` plus an inner patch
listing members means *forward only these*. Adding such a definition for a type currently forwarded
wholesale by `IsForward="true"` can take that type's other members out of the NodeBrowser — nodes
disappearing in silence, which is this project's signature failure. Do one, compile it, confirm in
the GUI that the tag works **and that its siblings are still there**, then do the next.

Two cheaper levers exist and should come first: **`Help.xml` accepts `tags`**, and XML doc comments
already become tooltips.

**Tag syntax: comma-separated.** The Design Guidelines say "a list of lower-case space-separated
terms", but across every `.vl` shipped with vvvv, **251 multi-term tags use commas and none use
spaces**. Follow the shipped code. The guidelines also say not to repeat "any term that is already
part of the nodes name, version or category" — a tag is for the word the user types *instead* of
the name.

---

## Help is the teaching surface, not a fatter node

The strongest single finding of the survey. Package after package carries far more help than nodes:

| pack | C# static nodes | help patches |
|---|---|---|
| VL.Skia | 4 | 98 |
| VL.ImGui | 7 | 90 |
| VL.Stride | 0 | 125 |
| VL.Stride.TextureFX | 0 | 70 |
| VL.OpenCV (community, 311 nodes) | 12 | 50 |
| VL.PolyTools (community, 317 nodes) | 0 | 75 |
| **VL.GIS (107 nodes)** | — | **4** |

Help patches run 16–24% of node count in the libraries people actually learn from. VL.GIS is at
3.7%, and every one of ours is a `HowTo`. There are five kinds, and they are not interchangeable:

| prefix | Gray Book's definition | shipped with vvvv |
|---|---|---|
| `Explanation` | "Typically a single patch per library giving an overview of the whole set of nodes" | 57 |
| `HowTo` | "A series of individual patches demonstrating how to achieve specific things" | 458 |
| `Reference` | "A patch covering the functionality of one specific node" | 102 |
| `Example` | "A patch more broadly showing a usecase of a library" | 67 |
| `Tutorial` | "Most often a link to a video tutorial" | — |

**The missing one is `Explanation`, and it is the front door**: one patch per library, showing the
whole node set. We have none, in either package. `Help.xml` (14 in vvvv's own packs) orders the
help browser independently of the filesystem and carries search tags.

So when the temptation is "this is hard to understand, let me make one node that does it all" —
the ecosystem's answer is the opposite: **keep the nodes small and spend the effort on a patch that
shows them working together.** A fat node teaches nothing; the patch is the teaching.

---

## Two library architectures, and which one this is

| | `.vl` size | node definitions | `Summary=` | `Tags=` |
|---|---|---|---|---|
| **Thin forwarder** — VL.GIS, VL.Mapsui, VL.Rhino.3dm, VL.Assimp | 2 KB | 0 (`IsForward="true"`) | 0 | 0 |
| **VL wrapper layer** — VL.OpenCV | 3.64 MB | 311 | 429 | 61 |
| — VL.PolyTools | 6.55 MB | 317 | 86 | 9 |

A thin forwarder is cheap and honest: the C# *is* the library, and node shape is C# signature shape.
A wrapper layer decouples the two — node granularity, pin names, defaults, summaries and tags all
become design decisions instead of consequences. The two biggest wrapper libraries in the ecosystem
both chose the wrapper layer, and it costs megabytes of `.vl`.

**VL.GIS stays a thin forwarder, with a wrapper definition only for front-door nodes** — the handful
a beginner meets first. Those get `Tags=` and `Summary=`; the other hundred stay forwarded. This is
a deliberate middle: the full wrapper layer is weeks of work in the one file format where our
mistakes are silent.

---

## Conventions worth obeying because the ecosystem already does

- **Async operations** expose `In Progress`, `On Completed`, `Success`, `Error`. Returning a bare
  `IObservable` is not wrong, but it makes every consumer invent the same four pins.
- **Defaults for imported types** come from a Forward operation called `CreateDefault` — the
  mechanism visible in `vvvvc`'s generated C# as `n21._Operations_.CreateDefault()`.
- **A path pin is `VL.Lib.IO.Path`, never `string`**, and a machine-dependent default is a node
  rather than a pin's initial value. Full reasoning in [DESIGN.md](DESIGN.md).
- **Angles are cycles** (0..1), colour components 0..1. Latitude and longitude stay in degrees —
  that is the domain's own unit, and every GIS source and sink speaks it.
- **Don't reference your own nuget** from any `.vl` that contributes to it, "other than: help
  patches".

---

## The gap between this document and the current code

Written down so it is a to-do list rather than a standard we quietly fail. Nothing here is a bug;
all of it is a library that works and reads like a C# API.

| | |
|---|---|
| **Front door** | No `Explanation Overview of available nodes.vl` in any of the three packages |
| **Ordering / search** | No `Help.xml` anywhere; no tags anywhere |
| **Readers named for us, not for the user** | `TileIndexParts`, `TileDestinationParts`, `MapViewInfo`, `CoordinateSystemInfo` → the ecosystem's word is `Split` |
| **Above four inputs** | `XyzTileSource` (6), `ToRendererSpace` (6 in / 4 out), `TileIndicesForBounds` (5), `CreateMapView` (5), `LonLatToLocal` (5), `HeightmapToMesh` (5), `SampleHeightmap` (5), `WebMercatorToLocal` (5), `CreateTileQuad` (5), `LineStringToRibbonMesh` (5), `ZoomByWheel` (5, VL.Mapsui) |
| **`Create` used loosely** | `CreateMapView`, `CreateBoundingBox` are containers of properties — join/split territory |
| **Subcategories** | `GIS.Mesh.Coordinates` / `.Tessellation` / `.Elevation` is three levels deep against "avoid excessive use of Subcategories" |
| **Async shape** | `FetchTileAsync` returns `IObservable<byte[]?>` instead of the four standard outputs |
