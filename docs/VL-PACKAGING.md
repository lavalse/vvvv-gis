# Packaging a library for vvvv gamma — field notes

Everything here was learned the expensive way. VL.GIS shipped **nine releases**
(`0.0.1`–`0.0.11`) that installed cleanly into vvvv and contributed **zero nodes**, with no
error message at any point — not in the log, not in the NodeBrowser, not in CI.

Three independent causes, each sufficient on its own, none of which produces a diagnostic.
That combination is why the problem survived nine attempts: each fix was real but the other
two causes kept the symptom identical, which made every correct change look wrong.

This document exists so none of that has to be rediscovered.

---

## Contents

- [The shape of a vvvv package](#the-shape-of-a-vvvv-package)
- [Trap 1 — document IDs](#trap-1--document-ids)
- [Trap 2 — the UTF-8 BOM](#trap-2--the-utf-8-bom)
- [Trap 3 — `[ImportAsIs]`](#trap-3--importasis)
- [Node categories](#node-categories)
- [The local verification loop](#the-local-verification-loop)
- [False proofs — verification that could not have failed](#false-proofs--verification-that-could-not-have-failed)
- [Smaller traps](#smaller-traps)
- [Environment reference](#environment-reference)
- [Debugging checklist](#debugging-checklist)

---

## The shape of a vvvv package

A vvvv package is a NuGet package with a specific internal layout:

```
VL.GIS/                 <- the package (folder name == package id)
├── VL.GIS.vl           <- entry point. MUST be at the root, no subfolder
├── VL.GIS.nuspec       <- required for vvvv to recognise a *source* (unpacked) package
├── lib/net8.0/*.dll
└── help/**
```

`build.ps1` stages exactly this under `dist\VL.GIS\`, which is byte-for-byte the layout an
installed package has under `%LOCALAPPDATA%\vvvv\gamma\nugets\VL.GIS.<version>\`. That
equivalence is the point: "works locally, breaks once published" cannot happen if the local
thing has the published shape.

### `--package-repositories` is one level up from the package

```powershell
vvvv.exe --package-repositories D:\2026_Projects\vvvv-gis\dist    # correct
vvvv.exe --package-repositories D:\2026_Projects\vvvv-gis\dist\VL.GIS   # wrong, silent
```

A *repository* is a directory containing one folder per package. Pointing it at the package
folder finds nothing and says nothing. An early version of the launch script (then
`test/test.ps1`, now `start.ps1`) pointed it at the repository root, where `VL.GIS.vl` sat
loose — same silent nothing.

Forgetting the switch entirely fails the same way, which is why `start.ps1` exists: it is
the only thing standing between a developer and half an hour spent wondering where the
nodes went.

### The entry point document

`VL.GIS.vl` is not source code you write; it is a serialized VL document. The reference
sample to copy is `<vvvv>\packs\VL.Stride\VL.Stride.vl` — 1692 bytes, and structurally the
same job as VL.GIS (wrap a pile of DLLs).

```xml
<Document Id="…22 chars…" LanguageVersion="2024.6.7-0009-ga0a8422da0" Version="0.128">
  <NugetDependency  Id="…" Location="NetTopologySuite" Version="2.6.0" />
  <Patch Id="…">
    <Canvas Id="…" DefaultCategory="GIS" CanvasType="FullCategory" />
    <Node Name="Application" …>…</Node>
  </Patch>
  <PlatformDependency Id="…" Location="./lib/net8.0/VL.GIS.Core.dll" IsForward="true" />
</Document>
```

| Element | Means |
|---|---|
| `<NugetDependency Location="X">` | pull in NuGet package `X` (third-party .NET packages go here) |
| `<PlatformDependency Location="./lib/…dll" IsForward="true">` | forward an assembly's nodes downstream — this is what makes them visible to consumers |
| `<ProjectDependency>` | reference a `.csproj` for hot-reload. **Never in a shipped `.vl`** |
| `<Patch>` + Application node | every real package has one; v0.0.11 deleted it |

`VL.CoreLib` is declared as a `<NugetDependency>` in the `.vl` but **must not** appear in
the `.nuspec` — it ships with vvvv. Same for `VL.Core` and `System.Reactive`; pinning the
latter makes vvvv log *"wasn't picked up because it's provided by vvvv itself"*.

---

## Trap 1 — document IDs

**Rule:** every `Id` is **exactly 22 characters**, first character in `[A-V]`, remaining 21
in `[0-9A-Za-z]`, and unique within the document. It is a base62-encoded 16-byte GUID; the
`[A-V]` restriction on the first character is what the encoding can produce there.

Verified against 21,823 IDs extracted from the VL packages installed on this machine —
100% conform.

The original hand-written `VL.GIS.vl` had 9 IDs, **5 of them malformed** (21 or 23 chars),
including the `<Document>` root itself:

| Id | Length | |
|---|---|---|
| `VLGISDoc1aB3cD5eF7gH9iJ` ← Document root | 23 | ❌ |
| `VLCoreLib1bC3dE5fG7hI9j` | 23 | ❌ |
| `ProjNt1cE3gG5iI7kK9mM` | 21 | ❌ |

A bad root ID means deserialization fails before anything else is read — which is why nine
rounds of adding and removing `<Patch>` elements changed nothing. **The fix was never in
the part being edited.**

**Now:** `tools\New-VLId.ps1` generates them (rejection sampling, so the distribution is
uniform), `tools\New-VLDocument.ps1` writes the document, and `tools\Test-VLPackage.ps1`
asserts format and uniqueness in CI.

**Never regenerate `VL.GIS.vl`.** IDs are identities. Changing them across releases changes
what the document *is*. Adding a dependency means appending one line with one fresh ID.

---

## Trap 2 — the UTF-8 BOM

`.vl` files must be UTF-8 **with** BOM (`EF BB BF`). Without it vvvv does not load the
document, and does not say so.

PowerShell 7 writes UTF-8 *without* BOM by default, so anything generating a `.vl` needs:

```powershell
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
[IO.File]::WriteAllText($path, $content, $utf8Bom)
```

Asserted by `Test-VLPackage.ps1`.

---

## Trap 3 — `[ImportAsIs]`

The worst of the three, because everything downstream of it reports success.

```csharp
using VL.Core.Import;
[assembly: ImportAsIs(Namespace = "VL")]
```

Without this attribute on a forwarded assembly:

- the package loads ✅
- it compiles ✅
- `vvvvc` exports it with zero warnings ✅
- `nuget pack` produces a valid package ✅
- **its public statics never appear in the NodeBrowser** ❌

They are demoted to raw .NET reflection nodes, which the NodeBrowser hides behind a
dependency toggle. To the user this is completely indistinguishable from the package having
failed to load.

**How it was found:** byte-comparing VL.GIS against `VL.Serialization.MessagePack` (a
minimal package that ships with vvvv and does the same job) and decoding its assembly
attribute blob. It was one line VL.GIS did not have.

**Now:** `tools\Test-VLImportAttribute.ps1` reads the attribute out of assembly metadata
via `System.Reflection.Metadata` — no assembly loading, so it works in CI — and it is
stage 0 of `verify.ps1`, deliberately first.

---

## Node categories

```
VL category = (.NET namespace − "VL" prefix) + type name
```

The prefix stripped is whatever `ImportAsIs(Namespace = …)` names. Two attributes adjust
the result:

| Attribute | Effect |
|---|---|
| `[Name("Geometry")]` | renames the type **for VL only**; the C# class stays `GeometryNodes` |
| `[SkipCategory]` | removes the type level, so members land directly in the namespace category |

```
namespace VL.GIS;        [Name("Geometry")]  class GeometryNodes   -> GIS.Geometry
namespace VL.GIS.Tiles;  [SkipCategory]      class TileFetchNodes  -> GIS.Tiles
namespace VL.GIS.Mesh;   [Name("Elevation")] class ElevationNodes  -> GIS.Mesh.Elevation
```

Two consequences worth remembering:

- **The assembly name is irrelevant to the category; the namespace is not.** Every project
  sets `<RootNamespace>VL.GIS</RootNamespace>` regardless of assembly name — otherwise
  `VL.GIS.Core` would yield `GIS.Core.Geometry`.
- **`[Name]` and `[SkipCategory]` arrived in gamma 7.2.** That is the only reason VL.GIS
  requires 7.2+. The alternative — renaming the C# classes themselves — would force an
  alias on NetTopologySuite's `Geometry` type through every signature that returns one.

---

## The local verification loop

The single biggest process change: **nuget.org was being used as a test environment.** A
published version can never be replaced, and the feedback cycle was hours. Everything is
local now.

```powershell
.\start.ps1                     # build, pick a document, launch vvvv against dist\
.\build.ps1                     # build + stage dist\VL.GIS\ only
.\test\verify.ps1               # headless, seconds
.\test\verify.ps1 -EndToEnd     # + pack to dist\feed + consume it from a separate document
.\tools\Test-VLPackage.ps1      # static only, no vvvv — what CI runs
```

| Stage | Proves |
|---|---|
| 0 | every forwarded assembly carries a `VL.Core.Import` attribute |
| 1 | the document deserializes, resolves dependencies and compiles (`vvvvc.exe`) |
| 2 | a *separate* document can consume the packed `.nupkg` (needs gamma ≥ 7.1) |

**What none of it proves:** that a node appears in the NodeBrowser under the expected
category (check once in the GUI), or that it computes the right answer (nothing checks this
yet — see the roadmap).

`Test-VLPackage.ps1` was negative-tested against the historically broken `VL.GIS.vl` and
correctly rejects it. A validator that has never failed on a known-bad input is not a
validator; the CI check it replaced passed all nine broken releases.

### Hot reload for C#

`test\dev.ps1` opens `test\DevLoop.vl`, which uses `<ProjectDependency>` to reference the
`.csproj` directly instead of the built `.dll`. Saving a `.cs` file recompiles and hotswaps
the running code — no rebuild, no restart. Static methods hotswap cleanly; stateful
instances lose their state on each save.

For breakpoints: attach Visual Studio to `vvvv.exe` and disable *Require source files to
exactly match the original version*, since hotswapped assemblies no longer match disk.

This is why the hot-reload document is separate from the shipped one — `ProjectDependency`
forces the package and everything downstream to stay editable.

---

## An invalid enum value is silently replaced by the default

The most expensive mistake in this repository after the packaging traps, and the same shape as
all of them: no error, no warning, nothing drawn.

VL.Skia's `CommonSpace` has **exactly four members**, and `Normalized` is the default:

| | |
|---|---|
| `Normalized` | the default; the visible area spans roughly 2.8 × 2 units |
| `DIP` | device-independent pixels |
| `DIPTopLeft` | device-independent pixels, origin top-left — what `GIS.Skia` wants |
| `Projection` | |

`PixelTopLeft` is **not one of them.** That string was harvested from
`VL.Stride.Rendering.TextureFX.vl`, where it belongs to a different enum on a pin named
`Common Screen Space`. Writing it into a `CommonSpace` IOBox leaves the space at `Normalized`,
so a tile positioned at pixel (278, 61) is drawn 278 units into a space two units tall — far
outside the window, and identically invisible whatever else you change. Three separate attempts
failed the same way for this one reason.

**Harvest enum values from the library that owns the enum.** Values were collected by scanning
every `.vl` that ships with vvvv, which mixed VL.Skia's `CommonSpace` with Stride's lookalike;
grouping the hits by file would have shown it immediately.

### Do not rely on a setting that fails silently — convert instead

Switching the value to `DIPTopLeft` would probably work, but it makes every patch depend on a
pin whose failure mode is invisible. `GIS.Skia.Viewport.ToRendererSpace` converts the numbers
instead, so the map draws in whatever space the renderer happens to be in — including the
default, unconfigured one:

```
scale = spaceHeight / view.Height          // 2 in the default space, hence the default
x     = (pixelX - view.Width / 2) * scale  // pixels from the top-left → units from the centre
```

For the tile at pixel (278, 61) in an 800 × 600 view: (−0.43, −0.80), size (0.85, 0.85) — inside
the 2.795 × 2 space, where 278 never could be. One scale serves both axes, taken from the
height, so a square tile stays square when the view's aspect ratio differs from the renderer's.

The patch now shows both rows of numbers, pixels above and units below, which makes the
conversion the visible subject of the example rather than a hidden detail.

**The general rule: when a setting's wrong value is indistinguishable from a broken patch,
prefer an arrangement that does not need the setting.** The same reasoning removed
`WithinCommonSpace` — one less node whose misconfiguration looks like nothing at all.

### `ClientBounds` is the probe; `Actual Bounds` is not

`Renderer.ClientBounds` reports the visible extent **in the current space's own units**, which
makes it a direct answer to "which space am I actually in":

| space | ClientBounds |
|---|---|
| `Normalized` | Pos (-1.3975, -1.00), Size (2.795, 2.00) |
| `DIPTopLeft` | Pos (0, 0), Size = the view in pixels |

`DrawImage.Actual Bounds` looks like the same kind of evidence and is not: it echoes the
position and size it was handed regardless of the space they are in, so it read a plausible
(278.26, 61.40) and (256, 256) throughout.

**Two more `DrawImage` pins must be set explicitly**, also silent when wrong:

| pin | values | want |
|---|---|---|
| `Size Mode` | `AutoHeight`, `AutoWidth`, `FitIn`, `OriginalSize`, `Size` | `Size` |
| `Anchor` | `TopLeft`, `TopRight`, `MiddleLeft`, `Center`, `MiddleRight`, `BottomLeft`, `BottomRight` | `TopLeft` |

**Two more pins on `DrawImage` have to be set explicitly**, and are silent when wrong:

| pin | values | want |
|---|---|---|
| `Size Mode` | `AutoHeight`, `AutoWidth`, `FitIn`, `OriginalSize`, `Size` | `Size` |
| `Anchor` | `TopLeft`, `TopRight`, `MiddleLeft`, `Center`, `MiddleRight`, `BottomLeft`, `BottomRight` | `TopLeft` |

### How this was found, because the symptom is useless on its own

Everything reported success: no red nodes, `vvvvc` exited 0, the structural validator passed,
and `DrawImage.Actual Bounds` gave Position (278.26, 61.40) and Size (256, 256) — all correct.
The window was simply black.

What separated the possibilities, in order:

1. **Give the Renderer a bright background colour.** It turned blue, which proved the Renderer
   was rendering and that its `Space` pin was connected — so the fault was in the layer, not
   the renderer. One IOBox, and it halved the search space.
2. **Read `DrawImage.Actual Bounds`.** Correct position and size meant `Size Mode`, `Anchor`
   and our own arithmetic were all fine, leaving only the space itself. Note the trap: that
   output echoes the position and size it was handed regardless of the space they are in, so
   plausible numbers there prove nothing about visibility.
3. **Read `ClientBounds`.** Pos (-1.3975, -1.00), Size (2.795, 2.00) — the space was
   `Normalized` all along, and the `Space` dropdown confirmed it by displaying `Normalized`
   rather than the value that had been written into it.
4. **Group the harvested enum values by source file.** `PixelTopLeft` came from a Stride file
   and no VL.Skia one, which is what made it invalid.

The lesson is about method rather than about Skia. Three rounds of reasoning over which enum
member *ought* to be correct lost to one screenshot of what the renderer actually reported.
When a value decides whether anything is visible at all, wire it to an IOBox, read the probe
that describes the result, and try every legal option — do not argue about which name sounds
right.

Note also why a passing probe had been misleading: rendering the same map to a PNG worked,
because that drew straight onto an `SKCanvas` in pixel coordinates with no VL space involved.
Another case of verifying in a host where the fault cannot occur.

## Do not patch a .vl in place — regenerate it

Adding a node by inserting XML into an existing document went wrong immediately: the anchor
pattern matched thirteen times, so thirteen copies were inserted and six IDs ended up
duplicated. vvvv had also rewritten the file in the background, replacing every ID, which
invalidated the pin IDs the edit was aiming at.

Keep a script that emits the whole document and re-run it. That is the same rule as
"never regenerate `VL.GIS.vl`, append one line" seen from the other side: an entry point's
IDs are identities that must persist across releases, while a help patch is disposable and is
safest rebuilt from a generator.

Either way, run the structural checks afterwards — count IDs, look for duplicates, confirm
every `<Link>` end resolves — because none of these mistakes produce an error on load.

## Never block on a task inside a node

`FetchTileBytes` shipped in `0.1.0-alpha` doing this:

```csharp
return tileSource.GetTileAsync(client, tileInfo).GetAwaiter().GetResult();
```

Running that node deadlocked vvvv. Awaiting on the calling thread captures its
`SynchronizationContext`, and vvvv's runtime thread has one, so the continuation was posted
back to the thread already sitting in `GetResult()` waiting for it.

What that looked like from the outside, none of it pointing at the cause:

- the runtime stopped evaluating — every output pin in the patch went blank
- `F5` would not restart it
- the log file was created and stayed **0 bytes**
- the window closed but `vvvv.exe` kept running, `Responding: True`
- `vvvvc` compiled and exported the same patch with no errors, because compiling never
  runs it

**The misleading verification.** Calling the node from PowerShell returned the tile
perfectly, 47967 bytes, every time. PowerShell installs no `SynchronizationContext`, so the
deadlock cannot occur there. An hour went into "the node works, so the patch must be
wrong". Exercising sync-over-async in a host without a context proves nothing about a host
that has one.

The fix is `Task.Run(() => ...).GetAwaiter().GetResult()` — the await then happens on a
thread pool thread with no context to post back to. It still stalls the frame for the
length of the request, so prefer the observable form (`FetchTileAsync`) in a patch.

`test\VL.GIS.Tests\FetchDeadlockTests.cs` covers this with a single-threaded
`SynchronizationContext` and a fake tile source, no network. One of its tests asserts that
the *original* pattern still fails to complete, so the harness cannot quietly stop being
sensitive.

### Diagnosing "the patch does nothing"

Blank outputs and wrong outputs are different symptoms with different causes. If **every**
output is blank, suspect evaluation, not logic:

1. Find one node that cannot fail — pure arithmetic, no I/O — and put an IOBox on it. Here
   `TileBounds` served: a value meant the runtime was alive, a blank meant it was not.
2. Check `F5` / `F7` / `F8` (run / pause / stop). `--stoppedonstartup` exists, so a stopped
   runtime is a real state.
3. Launch with **`--log`**, which writes to
   `%UserProfile%\Documents\vvvv\gamma\vvvv_<timestamp>.log`. An empty log is itself
   evidence: nothing ran.
4. Compile the document with `vvvvc`. Success means the patch is valid and the problem is
   at runtime.
5. Check whether outputs are even *connected*. A node with nothing on its output pins
   displays nothing, which reads exactly like a node that failed.

## An upstream library must be a package in a package repository

Declaring `<NugetDependency Location="BruTile" Version="6.0.0" />` in your `.vl` is necessary and
**not sufficient**. For VL to resolve that library's *types* — so a node whose signature mentions
`IHttpTileSource` can exist — the package has to be present in a package repository vvvv is
looking at.

Fail that and the symptom is the quietest yet: the node **is constructed**, none of its pins
connect, every link to it is silently dropped, and the compiled program does not contain the
call. `vvvvc` exits 0. Nothing is red. The log says nothing.

A real install satisfies this by accident — NuGet pulls your dependencies into
`%LOCALAPPDATA%\vvvv\gamma\nugets\` alongside your package, which is why `Rhino3dm`,
`AssimpNet`, `OpenCvSharp4` and `BruTile` are all sitting in there. **That folder is flat: one
version of each library, shared by everything vvvv loads, and it wins over a copy next to your
own assembly.** So this repository worked for months without knowing it depended on a package
installed there in February, and broke the moment that package was moved out to stop it clashing
with another project.

`build.ps1` now installs each `.vl`'s non-`VL.*` dependencies into `deps\`, transitively,
discovered rather than listed. Nothing here depends on what some other project happens to have
installed machine-wide.

**`deps\` is deliberately separate from `dist\`.** `--package-repositories` takes a
semicolon-separated list, and pointing it at a directory that *also* contains the document being
compiled makes `vvvvc` treat that document as a package rather than as something to build:
*"Entry point for document X.vl not found"*. Stage 1 of `verify.ps1` passes `deps\` only for
exactly this reason.

Two related facts worth having:

- **A consuming document must not declare the upstream package.** A help patch that adds
  `<NugetDependency Location="Mapsui" …/>` fails with *"Missing package: Mapsui"* — plain nugets
  are not VL packages. Only the wrapper package declares them.
- **`IsForward="true"` works on a `NugetDependency`, not just a `PlatformDependency`.**
  `VL.Rhino.3dm` does `<NugetDependency Location="Rhino3dm" IsForward="true" …/>` to expose that
  library's own members as nodes. VL.GIS does not need it — resolvability is enough for types on
  pins — but an earlier claim here that *no* package forwards anything but its own wrapper was
  wrong, and it was wrong because the survey behind it only looked at `PlatformDependency`.

### It wore three disguises

Worth recording, because each looked confirmed while the real cause was still in place:

| looked like | actually |
|---|---|
| "VL cannot build a pin for `IEnumerable<>` of a foreign interface" | It builds `Sequence<T>` fine; a single item was being wired into a spread |
| "these BruTile pins were broken all along" | They were fine. **My own control experiment was contaminated by my own action earlier that morning** |
| "no package forwards a foreign nuget" | `VL.Rhino.3dm` does |

The evidence that settled it was a pair of probe nodes differing in exactly one thing:

```csharp
ILayer? Update(int input)                // Update called, links attached   ✅
ILayer? Update(global::Mapsui.Map? map)  // Update never called             ❌
```

And once types resolved, vvvv's messages became precise immediately —
`ILayer is no Sequence<ILayer>!` rather than silence. **Silence was the tell.**

---

## False proofs — verification that could not have failed

The most costly failure mode in this project is not any single trap. It is running a check that
**cannot exhibit the bug**, then reporting the green result as evidence. It happened five times,
and every time it sent the investigation somewhere else for hours.

| The "proof" | Why it could not fail | What it cost |
|---|---|---|
| `FetchTileBytes` called from PowerShell returned 47967 bytes | PowerShell installs no `SynchronizationContext`, and the bug was a sync-over-async deadlock that needs one | Concluded "the node works, so the patch must be wrong" and searched the patch. The user found it instead, by reading a pin that went blank |
| A console probe rendered a correct OSM map of Tokyo to PNG | It drew straight onto an `SKCanvas` in pixel coordinates, with no VL space in the path — the whole thing that was broken | Reinforced three wrong diagnoses of the black window |
| `DrawImage.Actual Bounds` read (278.26, 61.40) / (256, 256) | That output echoes what it was handed, whatever space the numbers are in | Ruled out the arithmetic correctly, but read as "so the position is fine" |
| A probe checked that `Test-VLPackage.ps1` rejects a known-bad `.vl` and saw nothing | The validator reports via `Write-Host`, which does not go to stdout, so its failures were invisible to the probe | Nearly concluded the validator was broken when it was working |
| Two Mapsui dependency probes passed | Neither exercised the third constraint (BruTile 5 vs 6), and that one is an ABI break | Announced Mapsui as viable; retracted a day later |

Two rules come out of this, and they are worth more than any individual trap above:

**Before believing a green result, name the mechanism by which it could have gone red.** If you
cannot, the check proved nothing. A deadlock needs a `SynchronizationContext`; a space bug needs
VL's space; a validator's verdict needs to reach the thing reading it (use exit codes, not
console output).

**Verify in the host that can actually fail.** For this repository that means vvvv, which is why
`start.ps1` exists and why "only the GUI proves a node appears" is stated three separate times.

### The mirror image: a failing test whose expectation is wrong

Four test failures in this project were the *test* being wrong, not the library:

- a ratio of ~1e12 asserted to three decimal places
- `LonLatToWebMercator(0, 0)` returning −7.1e−10, because `tan(π/4)` is `0.9999999999999999` — sub-nanometre, and correct
- `TessellatePolygon` emitting 6 vertices for a square: NTS's `DelaunayTriangulationBuilder` does not merge coincident corners (and the credit for that belonged to NTS, not LibTessDotNet as first written)
- a space's left edge asserted at −1.3975 for a **square** view, where it is −1

So the rule cuts both ways: **check the expectation before changing the code.** Deriving the
expected number from the definition takes a minute and it is the same minute either way.

---

## Smaller traps

**NuGet versions are immutable, including in the local cache.** Repacking `0.1.0` after
changing its contents does nothing: the next consumer reads the stale copy out of
`~\.nuget\packages\vl.gis\0.1.0`. This produced a real, correctly-reported failure
("consumer export missing VL.GIS.Tiles.dll") that looked like a false alarm. `pack.ps1` now
deletes that directory on every pack, and `verify.ps1 -EndToEnd` wipes `dist\_e2e` first.

**vvvv holds staged assemblies open.** `build.ps1` throws if vvvv is running rather than
producing a confusing file-lock error. Rebuilding under a running vvvv would not update
loaded nodes anyway.

**`vvvvc.exe` requires absolute paths** — "The file path must be absolute". Resolve with
`Resolve-Path` / `[IO.Path]::GetFullPath`.

**`--export-package-sources` was broken before gamma 7.1** (NU1102). If stage 2 fails on an
old install, that is why.

**Installing a package ≠ referencing it.** A blank patch does not reference VL.GIS, so
searching for its nodes finds nothing — and the NodeBrowser will helpfully offer to install
the package from nuget.org instead, which is how a broken published version got pulled in
during testing. `start.ps1` always opens a document that already declares the dependency,
rather than a blank patch, for exactly this reason. In the GUI: `Ctrl+J` → Solution Explorer
→ Dependencies, then **right-click** the entry to toggle it.

**The log window is `Ctrl+Shift+F2`.** (Not `Ctrl+Shift+L`.)

**26 gamma versions are installed on this machine**, with names like `vvvv_gamma_2021.4.12`
and `vvvv_gamma_7.4-win-x64`. A lexical sort picks the wrong one. `tools\Find-Vvvv.ps1`
parses the version numerically and enforces `$MinimumVersion = '7.2'`.

**`nuget pack` and an extensionless file.** `<file src="LICENSE" target="docs\" />` produces
`docs\LICENSE.txt\LICENSE` — NuGet reads the extensionless target as a directory. LICENSE is
not packed; `<license type="expression">MIT</license>` is what nuget.org displays anyway.

**No XML comment anywhere can contain `--`.** It is an XML rule, not an MSBuild one, so it bites
in csproj (as MSB4025), in a `.vl`, and in any script that generates either. Hit twice while
writing explanatory comments with an em-dash typed as `--`; the second time it produced a
150-node document that failed to parse. Any generator should check its own output:

```powershell
foreach ($m in [regex]::Matches($xml, '(?s)<!--(.*?)-->')) {
    if ($m.Groups[1].Value -match '--') { throw "XML comment contains --" }
}
```

**A help patch must ship in the package it depends on, not the one it demonstrates.**
`VL.GIS.nuspec` packed `help\**\*.vl`, which swept in an example needing `VL.GIS.Skia`; anyone
installing `VL.GIS` alone would open it to a missing dependency, and `VL.GIS.Skia` shipped no
example at all. `help\` is now split into one folder per package and staged as `help\`, which is
where vvvv looks. A patch belongs to the **last** package it depends on.

**`ImageLayer` is internal to VL.Skia.** `vvvvc` rejects it with "Not found: ImageLayer" even
though it appears in VL.Skia's own documents. Use `DrawImage`, which takes a `Vector2` position
and size rather than an `SKRect`. That is also why `TileDestinationParts` exists alongside
`TileDestination`: an `SKRect` has no node to open it, so a patch author handed one could see
neither the numbers nor a way to use them.

**A help patch can be hand-authored.** Claiming otherwise was wrong. `vvvvc` validates node and
pin names, so a generated document that compiles is structurally sound — negative-tested with a
misspelled pin and an unknown node, both rejected. What it cannot check is whether the patch
means anything, which is what the GUI is for.

**`dotnet pack` is the wrong tool.** It packs per-project. This package is defined by a
hand-maintained `.nuspec` and needs `nuget pack VL.GIS.nuspec`.

---

## Environment reference

| Thing | Path |
|---|---|
| vvvv gamma | `C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\` |
| headless compiler | `<vvvv>\vvvvc.exe` |
| bundled NuGet | `<vvvv>\tools\NuGet.exe` |
| reference packages | `<vvvv>\packs\` — `VL.Stride`, `VL.CoreLib`, `VL.Serialization.MessagePack` are the useful samples |
| installed packages | `%LOCALAPPDATA%\vvvv\gamma\nugets\` |
| NuGet global cache | `%USERPROFILE%\.nuget\packages\` |
| log, with `--log` | `%USERPROFILE%\Documents\vvvv\gamma\vvvv_<timestamp>.log` |

Useful `vvvv.exe` switches beyond `--package-repositories`: `--log` (log to disk),
`--stoppedonstartup` (do not start the runtime), `--nocache` (recompile packages from
source), `--editable-packages`, `-m` (allow a second instance). `--help` lists the rest.

`%LOCALAPPDATA%\vvvv\gamma\_nugets-backup-VL.GIS\` holds the seven broken local installs
(0.0.4, 0.0.6–0.0.11). They were **moved, not deleted** — 0.0.6 through 0.0.11 were never
published, so those folders are the only surviving copies. Keep them as forensic samples.

Published state, as of 2026-08-11: **`0.2.0-alpha` is the only listed version.** `0.1.0-alpha`
and `0.0.1`–`0.0.4` are unlisted. nuget.org supports unlisting only, never deletion
(`dotnet nuget delete` performs an unlist there), and unlisting is *silent* — an existing
consumer keeps resolving the version with no notice. Deprecating instead attaches a reason that
shows up in the client, which is the better tool for a version that actively breaks vvvv, as
`0.1.0-alpha` does.

`VL.GIS.Skia` has never been published. It shares VL.GIS's version number deliberately, and
`publish.yml` pushes dependencies first.

The repository moved from `lavalse/vvvv-gis` to `rednotfound/vvvv-gis`; the old URL redirects,
but the `projectUrl` baked into the published `0.2.0-alpha` still points at it and cannot be
changed — package metadata is as immutable as the package.

---

## Debugging checklist

When a package's nodes do not appear, in this order — cheapest and most likely first:

1. Does the consuming document actually **reference** the package? (`Ctrl+J` → Dependencies)
2. Does every forwarded assembly carry **`[ImportAsIs]`**? → `Test-VLImportAttribute.ps1`
3. Are all `.vl` **IDs 22 chars** and unique, and is the file **BOM'd**? → `Test-VLPackage.ps1`
4. Does the `.vl` still have its **`<Patch>` + Application node**?
5. Does the nuspec ship the `.vl` **at the package root** and every forwarded DLL?
6. Is `--package-repositories` pointed at the **repository**, one level above the package?
7. Is a **stale copy** cached in `~\.nuget\packages\` or `%LOCALAPPDATA%\vvvv\gamma\nugets\`?
8. Then, and only then, read the log — `Ctrl+Shift+F2`.

The meta-lesson: **change one variable per round.** The nine failed releases each changed
the `.vl`, the csproj and the nuspec together, so no round produced usable information.

And when a patch draws nothing rather than failing to load, add one more question before all of
the above: **is the check you are about to run capable of failing?** See
[False proofs](#false-proofs--verification-that-could-not-have-failed).
