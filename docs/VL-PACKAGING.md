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
folder finds nothing and says nothing. An early version of `test/test.ps1` pointed it at the
repository root, where `VL.GIS.vl` sat loose — same silent nothing.

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
.\build.ps1                     # build + stage dist\VL.GIS\
.\test\verify.ps1               # headless, seconds
.\test\verify.ps1 -EndToEnd     # + pack to dist\feed + consume it from a separate document
.\test\test.ps1                 # launch vvvv against dist\, open SmokeTest.vl
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
during testing. `test.ps1` opens `SmokeTest.vl`, which already declares the dependency.
In the GUI: `Ctrl+J` → Solution Explorer → Dependencies.

**The log window is `Ctrl+Shift+F2`.** (Not `Ctrl+Shift+L`.)

**26 gamma versions are installed on this machine**, with names like `vvvv_gamma_2021.4.12`
and `vvvv_gamma_7.4-win-x64`. A lexical sort picks the wrong one. `tools\Find-Vvvv.ps1`
parses the version numerically and enforces `$MinimumVersion = '7.2'`.

**`nuget pack` and an extensionless file.** `<file src="LICENSE" target="docs\" />` produces
`docs\LICENSE.txt\LICENSE` — NuGet reads the extensionless target as a directory. LICENSE is
not packed; `<license type="expression">MIT</license>` is what nuget.org displays anyway.

**MSBuild XML comments cannot contain `--`** (MSB4025). Relevant because the csproj files
carry long explanatory comments.

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

`%LOCALAPPDATA%\vvvv\gamma\_nugets-backup-VL.GIS\` holds the seven broken local installs
(0.0.4, 0.0.6–0.0.11). They were **moved, not deleted** — 0.0.6 through 0.0.11 were never
published, so those folders are the only surviving copies. Keep them as forensic samples.

Published state: `0.1.0-alpha` is the only listed version. `0.0.1`–`0.0.4` are unlisted
(nuget.org supports unlisting only, never deletion; `dotnet nuget delete` performs an
unlist there).

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
