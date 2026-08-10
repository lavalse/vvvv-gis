using VL.Core.Import;

// Required for the public statics of this assembly to appear as nodes. Forwarding the
// .dll from VL.GIS.vl is necessary but not sufficient -- see src/VL.GIS.Core/AssemblyInfo.cs.
//
// Namespace VL.GIS.Mesh minus the "VL" prefix gives GIS.Mesh, plus the [Name] on each
// class: GIS.Mesh.Coordinates, GIS.Mesh.Tessellation, GIS.Mesh.Elevation.
[assembly: ImportAsIs(Namespace = "VL")]
