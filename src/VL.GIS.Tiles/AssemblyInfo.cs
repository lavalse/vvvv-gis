using VL.Core.Import;

// Required for the public statics of this assembly to appear as nodes. Forwarding the
// .dll from VL.GIS.vl is necessary but not sufficient -- see src/VL.GIS.Core/AssemblyInfo.cs.
//
// Namespace VL.GIS.Tiles minus the "VL" prefix gives the category GIS.Tiles; the classes
// carry [SkipCategory] so their members land there directly rather than under
// GIS.Tiles.TileProviderNodes and GIS.Tiles.TileFetchNodes.
[assembly: ImportAsIs(Namespace = "VL")]
