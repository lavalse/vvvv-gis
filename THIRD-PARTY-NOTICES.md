# Third-party notices

VL.GIS itself is MIT (see [LICENSE](LICENSE)). It does not redistribute the libraries
below — NuGet resolves each one as a separate package under its own licence — but you are
running their code, so their terms apply to you as well.

| Package | Version | Licence | Role |
|---|---|---|---|
| [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) | 2.6.0 | BSD-3-Clause | geometry model and spatial operations |
| [NetTopologySuite.IO.GeoJSON](https://github.com/NetTopologySuite/NetTopologySuite.IO.GeoJSON) | 3.0.0 | BSD-3-Clause | GeoJSON read/write |
| [ProjNet](https://github.com/NetTopologySuite/ProjNet4GeoAPI) | 2.1.0 | **LGPL-2.1-or-later** | coordinate reprojection |
| [BruTile](https://github.com/BruTile/BruTile) | 6.0.0 | Apache-2.0 | tile schemas and tile sources |

`VL.Core` is referenced at compile time only; it ships with vvvv gamma and is not a
package dependency of VL.GIS.

## ProjNet and the LGPL

ProjNet is the one component that is not permissively licensed. MIT code with an LGPL
dependency is a normal, well-established arrangement: LGPL-2.1 §6 permits use from a
program under other terms as long as the user can relink against a modified version of
the library.

A normal vvvv export produces a folder of loose assemblies. That is dynamic linking, §6 is
satisfied, and there is nothing further to do.

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
  requires an identifying User-Agent (VL.GIS sets one) and forbids bulk downloading. Heavy
  or automated use needs your own tile source.
- **Attribution** is generally required. `TileAttribution` returns what the source declares;
  display it.
