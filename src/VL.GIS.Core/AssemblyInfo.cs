using VL.Core.Import;

// Makes every public type and its members show up as ordinary VL nodes.
//
// Without this, forwarding the .dll from VL.GIS.vl still "works" -- the package loads and
// compiles -- but the methods are only reachable as raw .NET reflection nodes, which the
// NodeBrowser hides behind a dependency toggle. Searching for a node by name finds
// nothing, which looks exactly like the package failing to load.
//
// Namespace = "VL" is the prefix stripped when deriving the VL category from the .NET
// namespace, so VL.GIS.Core.GeometryNodes lands under GIS.Core.GeometryNodes.
// This mirrors VL.Serialization.MessagePack, which ships with vvvv and uses the same
// single line.
[assembly: ImportAsIs(Namespace = "VL")]
