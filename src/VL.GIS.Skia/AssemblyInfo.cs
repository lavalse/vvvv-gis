using VL.Core.Import;

// Without this the assembly forwards cleanly and contributes no nodes at all. See
// docs/VL-PACKAGING.md -- this single missing line accounted for several of the releases
// that installed and did nothing.
[assembly: ImportAsIs(Namespace = "VL")]
