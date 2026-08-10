using VL.Core.Import;

namespace VL.GIS;

/// <summary>
/// Package metadata nodes.
/// </summary>
[Name("About")]
public static class AboutNodes
{
    /// <summary>The version of the VL.GIS package that is currently loaded.</summary>
    public static string GisVersion() => "0.1.0";
}
