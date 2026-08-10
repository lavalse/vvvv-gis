# Third-party notices

Almost all of the actual work here belongs to other people. VL.GIS is a thin wrapper: it
exposes existing .NET GIS libraries as vvvv nodes and adds very little of its own. The
geometry engine, the projection maths and the tile handling are all upstream.

VL.GIS itself is MIT (see [LICENSE](LICENSE)). The libraries below keep their own terms.

## Libraries

### NetTopologySuite 2.6.0 — BSD-3-Clause

> Copyright © 2006 - 2025 NetTopologySuite - Team, Diego Guidi,
> John Diss (www.newgrove.com), Felix Obermaier (www.ivv-aachen.de),
> Todd Jackson, Joe Amenta

https://github.com/NetTopologySuite/NetTopologySuite — the geometry model and every
spatial operation in `GIS.Geometry`, plus the WKT/WKB readers in `GIS.Serialization`.

### NetTopologySuite.IO.GeoJSON 3.0.0 — BSD-3-Clause

> Copyright © 2022 NetTopologySuite - Team

https://github.com/NetTopologySuite/NetTopologySuite.IO.GeoJSON — GeoJSON read/write.

### ProjNet 2.1.0 — LGPL-2.1-or-later

> Copyright © 2006 - 2025 NetTopologySuite - Team, Morten Nielsen

https://github.com/NetTopologySuite/ProjNet4GeoAPI — everything in `GIS.Projection`.
See [the LGPL section](#projnet-and-the-lgpl) below.

### BruTile 6.0.0 — Apache-2.0

> Copyright © BruTile Developers Team 2008-2025
> Paul den Dulk, Felix Obermaier

https://github.com/BruTile/BruTile — tile schemas, tile sources and fetching in
`GIS.Tiles`.

`VL.Core` is referenced at compile time only. It ships with vvvv gamma and is not a
package dependency of VL.GIS.

## Who has to reproduce these notices

Worth being precise, because the answer differs for you and for your users:

**The VL.GIS package does not redistribute any of the above.** NuGet resolves each as a
separate package with its own licence, so installing VL.GIS triggers no attribution
obligation on its own.

**Exporting a vvvv application does redistribute them.** The export folder contains
`NetTopologySuite.dll`, `ProjNET.dll`, `BruTile.dll` and friends as real binaries. At that
point BSD-3-Clause and Apache-2.0 both require you to reproduce their copyright notices
"in the documentation and/or other materials provided with the distribution" — so ship
this file, or an equivalent notice, alongside anything you export that uses VL.GIS.

## ProjNet and the LGPL

ProjNet is the one component that is not permissively licensed. MIT code with an LGPL
dependency is a normal, well-established arrangement: LGPL-2.1 §6 permits use from a
program under other terms as long as the user can relink against a modified version of
the library.

A normal vvvv export produces a folder of loose assemblies. That is dynamic linking, §6 is
satisfied, and there is nothing further to do beyond the attribution above.

The obligation only becomes live if you export in a way that **statically fuses** ProjNet
into your application — AOT, single-file, ILMerge or aggressive trimming. In that case
§6 requires you to enable relinking (for example by also shipping object files, or the
unfused assemblies). Most vvvv work never hits this; commercial installation work
occasionally does.

If you want to avoid the question entirely, use only the `GIS.Geometry`,
`GIS.Serialization`, `GIS.Tiles` and `GIS.Mesh` categories and leave `GIS.Projection`
alone — reprojection is the only thing that touches ProjNet.

## Tile usage policies

`GIS.Tiles` can point at third-party tile servers. Those servers set their own terms, and
they are not covered by any licence above:

- **OpenStreetMap** — the [tile usage policy](https://operations.osmfoundation.org/policies/tiles/)
  requires an identifying User-Agent (VL.GIS sets one) and forbids bulk or scripted
  downloading. Anything beyond light interactive use needs your own tile source.
- **Attribution is required**, and it is about the *data*, not the software: OpenStreetMap
  data is © OpenStreetMap contributors, licensed ODbL. `TileAttribution` returns whatever
  the source declares — display it.
