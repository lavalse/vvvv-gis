# Builds examples\Example Map with data on it.vl - the patch that proves the two packages compose.
#
# Scaffolding only. Once the patch runs, the checked-in .vl is the source of truth and this script
# is not run again: regenerating would discard any layout arranged by hand in the GUI.
#
# It starts from VL.Mapsui's own "HowTo Show a map.vl" rather than assembling a renderer chain from
# scratch, because that patch is already known to run. What it adds is four nodes: VL.GIS computes
# a geometry, and VL.Mapsui turns it into a layer the map draws.
#
# An earlier version of this script did something else, and it is worth recording why that was
# wrong. It read Mapsui's viewport, converted the resolution to a zoom, built a VL.GIS MapView from
# it, turned the geometry into an SKPath in pixels, and drew that over the map with DrawPath inside
# a WithinCommonSpace. Nine nodes, two coordinate systems kept in step by hand, and nothing
# appeared on screen. The right shape is the obvious one: **the geometry becomes an ILayer and goes
# into the map**, where Mapsui projects and draws it like any other layer.
$ErrorActionPreference = 'Stop'

$Root    = Split-Path $PSScriptRoot -Parent
$Base    = 'D:\2026_Projects\vl-mapsui\help\VL.Mapsui\HowTo Show a map.vl'
$OutDir  = Join-Path $Root 'examples'
$OutFile = Join-Path $OutDir 'Example Map with data on it.vl'
$NewId   = { & (Join-Path $PSScriptRoot 'New-VLId.ps1') -Count 1 }

if (-not (Test-Path $Base)) { throw "Base patch not found: $Base" }
New-Item -ItemType Directory -Force $OutDir | Out-Null

$raw = [IO.File]::ReadAllText($Base)

# Every insertion anchors on a match that must occur exactly once. Editing a .vl in place once
# produced thirteen duplicated nodes here; failing loudly is what stops that repeating.
function Insert-Once([string]$text, [string]$pattern, [string]$replacement) {
    $m = [regex]::Matches($text, $pattern)
    if ($m.Count -ne 1) { throw "anchor matched $($m.Count) times, expected 1: $pattern" }
    $text.Substring(0, $m[0].Index) + $replacement + $text.Substring($m[0].Index + $m[0].Length)
}

# A copied document needs its own Id; two documents claiming one identity fails quietly. Internal
# pin and pad Ids are document-local and may stay as they are.
$raw = Insert-Once $raw '<Document xmlns:p="property" xmlns:r="reflection" Id="[^"]+"' `
    ('<Document xmlns:p="property" xmlns:r="reflection" Id="' + (& $NewId) + '"')

# Both packages, which is the whole point. 0.0.0 means "whatever the package repository holds"
# rather than pinning a version this patch would then demand forever.
$raw = Insert-Once $raw '(?m)^\s*<NugetDependency [^>]*Location="VL\.Mapsui"[^>]*/>\r?\n' (
    '  <NugetDependency Id="' + (& $NewId) + '" Location="VL.Mapsui" Version="0.0.0" />' + "`r`n" +
    '  <NugetDependency Id="' + (& $NewId) + '" Location="VL.GIS" Version="0.0.0" />' + "`r`n")

function PinId([string]$nodeName, [string]$pinName) {
    $node = [regex]::Match($raw, "(?s)<Node Bounds=`"[^`"]*`" Id=`"[^`"]*`">\s*<p:NodeReference[^>]*>(?:(?!</Node>).)*?Name=`"$nodeName`"[^>]*/>.*?</Node>")
    if (-not $node.Success) { throw "node not found: $nodeName" }
    $pin = [regex]::Match($node.Value, "<Pin Id=`"([^`"]+)`" Name=`"$pinName`"")
    if (-not $pin.Success) { throw "pin not found: $nodeName.$pinName" }
    $pin.Groups[1].Value
}

# The map's layer list already has a free second input, so the overlay needs no new Cons there.
$layersInput2 = PinId 'Cons' 'Input 2'

$ids = @{}
foreach ($k in @(
    'pointNode','pointLon','pointLat','pointOut',
    'bufferNode','bufferIn','bufferDist','bufferSeg','bufferOut',
    'consNode','consIn','consIn2','consOut',
    'geoNode','geoGeoms','geoFill','geoLine','geoWidth','geoBuilt','geoOut',
    'padLon','padLat','padRadius','padBuilt')) { $ids[$k] = & $NewId }

$branch = @"
          <!--
            ************************ data on the map ************************

            Everything above is VL.Mapsui drawing a basemap. These four nodes are the other half:
            VL.GIS computes a geometry, and Mapsui.Layers.Geometry turns it into a layer that goes
            into the same Map as the tiles.

            **The two packages meet through NetTopologySuite, not through each other.** Neither
            references the other. NTS is the vocabulary they already share, which is also why the
            same node draws geometry from any other source. Until BruTile was taken out of VL.GIS
            they could not even be installed side by side: vvvv keeps one version of each library
            for everything it loads, and the two needed different BruTiles.

            Coordinates are WGS84 lon/lat, which is what VL.GIS produces; the layer projects to
            spherical mercator on the way in, because which projection the map draws in is not a
            decision a patch should have to take.

            Buffer's distance is in the CRS's own units, so here it is degrees: 0.002 is roughly
            180 metres at this latitude. Getting that wrong is quiet. The first version of this
            patch used 0.02, which spans about 1.8 km and covers the whole window, and it put the
            ring over Tokyo while the map opened over Kansai. Neither mistake looks like a mistake
            on screen; both just look like nothing being drawn.
          -->
          <Node Bounds="420,700,120,19" Id="$($ids.pointNode)">
            <p:NodeReference LastCategoryFullName="GIS.Geometry" LastDependency="VL.GIS.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="OperationCallFlag" Name="CreatePoint" />
            </p:NodeReference>
            <Pin Id="$($ids.pointLon)" Name="Longitude" Kind="InputPin" />
            <Pin Id="$($ids.pointLat)" Name="Latitude" Kind="InputPin" />
            <Pin Id="$($ids.pointOut)" Name="Result" Kind="OutputPin" />
          </Node>
          <Pad Id="$($ids.padLon)" Comment="Longitude" Bounds="300,660,62,15" ShowValueBox="true" isIOBox="true" Value="135.79" />
          <Pad Id="$($ids.padLat)" Comment="Latitude" Bounds="300,682,62,15" ShowValueBox="true" isIOBox="true" Value="34.94" />

          <Node Bounds="420,740,100,19" Id="$($ids.bufferNode)">
            <p:NodeReference LastCategoryFullName="GIS.Geometry" LastDependency="VL.GIS.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="OperationCallFlag" Name="Buffer" />
            </p:NodeReference>
            <Pin Id="$($ids.bufferIn)" Name="Geometry" Kind="InputPin" />
            <Pin Id="$($ids.bufferDist)" Name="Distance" Kind="InputPin" />
            <Pin Id="$($ids.bufferSeg)" Name="Segments" Kind="InputPin" />
            <Pin Id="$($ids.bufferOut)" Name="Output" Kind="OutputPin" />
          </Node>
          <Pad Id="$($ids.padRadius)" Comment="Radius (degrees)" Bounds="300,740,62,15" ShowValueBox="true" isIOBox="true" Value="0.002" />

          <!-- A layer takes a spread of geometry; this one has a single shape in it. -->
          <Node Bounds="420,780,39,19" Id="$($ids.consNode)">
            <p:NodeReference LastCategoryFullName="Collections.Spread" LastDependency="VL.CoreLib.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="OperationCallFlag" Name="Cons" />
              <CategoryReference Kind="RecordType" Name="Spread" NeedsToBeDirectParent="true" />
            </p:NodeReference>
            <Pin Id="$($ids.consIn)" Name="Input" Kind="InputPin" />
            <Pin Id="$($ids.consIn2)" Name="Input 2" Kind="InputPin" />
            <Pin Id="$($ids.consOut)" Name="Result" Kind="OutputPin" />
          </Node>

          <Node Bounds="420,820,150,19" Id="$($ids.geoNode)">
            <p:NodeReference LastCategoryFullName="Mapsui.Layers" LastDependency="VL.Mapsui.vl">
              <Choice Kind="NodeFlag" Name="Node" Fixed="true" />
              <Choice Kind="ProcessAppFlag" Name="Geometry" />
            </p:NodeReference>
            <Pin Id="$($ids.geoGeoms)" Name="Geometries" Kind="InputPin" />
            <Pin Id="$($ids.geoFill)" Name="Fill Color" Kind="InputPin" />
            <Pin Id="$($ids.geoLine)" Name="Line Color" Kind="InputPin" />
            <Pin Id="$($ids.geoWidth)" Name="Line Width" Kind="InputPin" />
            <Pin Id="$($ids.geoBuilt)" Name="Layers Built" Kind="OutputPin" />
            <Pin Id="$($ids.geoOut)" Name="Result" Kind="OutputPin" />
          </Node>
          <Pad Id="$($ids.padBuilt)" Comment="Layers Built (geometry)" Bounds="600,820,140,15" ShowValueBox="true" isIOBox="true" />
"@

$raw = Insert-Once $raw '(?m)^\s*</Canvas>' ($branch + "`r`n        </Canvas>")

$links = @(
    @($ids.padLon,    $ids.pointLon),
    @($ids.padLat,    $ids.pointLat),
    @($ids.pointOut,  $ids.bufferIn),
    @($ids.padRadius, $ids.bufferDist),
    @($ids.bufferOut, $ids.consIn),
    @($ids.consOut,   $ids.geoGeoms),
    @($ids.geoBuilt,  $ids.padBuilt),
    @($ids.geoOut,    $layersInput2)
)
$linkXml = ($links | ForEach-Object { '        <Link Id="' + (& $NewId) + '" Ids="' + $_[0] + ',' + $_[1] + '" />' }) -join "`r`n"
$raw = Insert-Once $raw '(?m)^\s*</Patch>\s*\r?\n\s*</Node>' ($linkXml + "`r`n      </Patch>`r`n    </Node>")

$null = [xml]$raw
[IO.File]::WriteAllText($OutFile, $raw, (New-Object System.Text.UTF8Encoding($true)))

$ids2 = @([regex]::Matches($raw, 'Id="([^"]*)"') | ForEach-Object { $_.Groups[1].Value })
$bad  = @($ids2 | Where-Object { $_ -notmatch '^[A-V][0-9A-Za-z]{21}$' })
$dup  = @($ids2 | Group-Object | Where-Object { $_.Count -gt 1 })
$known = @([regex]::Matches($raw, '<(?:Pin|Pad) Id="([^"]*)"') | ForEach-Object { $_.Groups[1].Value })
$dangling = 0
foreach ($l in [regex]::Matches($raw, '<Link [^>]*Ids="([^,]+),([^"]+)"')) {
    foreach ($e in $l.Groups[1].Value, $l.Groups[2].Value) { if ($e -notin $known) { $dangling++ } }
}
Write-Host "  $OutFile"
Write-Host "  ids $($ids2.Count), malformed $($bad.Count), duplicated $($dup.Count), dangling links $dangling"
if ($bad.Count -or $dup.Count -or $dangling) { throw "structure check failed" }
