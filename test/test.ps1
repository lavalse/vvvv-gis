<#
.SYNOPSIS
    Launches vvvv gamma with the locally staged VL.GIS package loaded.

.DESCRIPTION
    Points vvvv at dist\ as a source package repository. Note the nesting: the argument
    is the *repository* (dist\), which contains one folder per package (dist\VL.GIS\).
    Passing the package folder itself, or the repo root, does not work.

    Nothing is published or installed -- vvvv reads the package straight off disk, and
    prefers a source package over an installed nuget of the same name.

    Opens test\SmokeTest.vl, which already declares

        <NugetDependency Location="VL.GIS" Version="0.1.0" />

    That matters: a package sitting in a repository is *available*, not *referenced*.
    A blank patch does not depend on VL.GIS, so its nodes do not show up in the
    NodeBrowser, and searching for one there instead offers to fetch the package from
    nuget.org -- which is the wrong VL.GIS entirely. Opening a document that already
    declares the dependency avoids that trap.

    Run .\build.ps1 first (or pass -Build).

.EXAMPLE
    .\build.ps1; .\test\test.ps1

.EXAMPLE
    .\test\test.ps1 -Build -Editable
#>
param(
    [string]$VvvvPath = '',
    [switch]$Build,
    # Loads VL.GIS from source so patches inside it stay editable; packages are
    # read-only by default.
    [switch]$Editable,
    # Start from an empty patch instead of SmokeTest.vl. You will then have to add the
    # VL.GIS dependency yourself via Ctrl+J > Dependencies.
    [switch]$Blank
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$Dist     = Join-Path $RepoRoot 'dist'

if ($Build) {
    & (Join-Path $RepoRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed" }
}

if (-not (Test-Path (Join-Path $Dist 'VL.GIS\VL.GIS.vl'))) {
    throw "dist\VL.GIS\VL.GIS.vl not found. Run .\build.ps1 first."
}

if (-not $VvvvPath) {
    $VvvvPath = & (Join-Path $RepoRoot 'tools\Find-Vvvv.ps1')
}
if (-not (Test-Path $VvvvPath)) {
    throw "vvvv.exe not found at '$VvvvPath'. Pass -VvvvPath explicitly."
}

$vvvvArgs = @('--package-repositories', $Dist)
if ($Editable) { $vvvvArgs += @('--editable-packages', 'VL.GIS') }

$doc = Join-Path $PSScriptRoot 'SmokeTest.vl'
if (-not $Blank) {
    if (-not (Test-Path $doc)) { throw "$doc not found." }
    $vvvvArgs += @('--open', $doc)
}

Write-Host "vvvv       : $VvvvPath"
Write-Host "repository : $Dist"
Write-Host "document   : $(if ($Blank) { '(blank patch)' } else { $doc })"
Write-Host @"

In vvvv:
  1. Double left-click empty canvas -> NodeBrowser
  2. Type  GisVersion   -- smoke-test node, proves the package loaded
  3. Type  CreatePoint  -- a real GIS node
  4. Drag from an output pin, then Alt+left-click empty canvas -> IOBox showing the value

  Do NOT accept a NodeBrowser offer to download/install "VL.GIS" -- that is the old
  broken package on nuget.org. The one under test is loaded from dist\ already.

  Ctrl+Shift+F2  Log window (look for red entries mentioning VL.GIS)
  Ctrl+J         Solution Explorer (Dependencies live here)
  Ctrl+U         Solution Explorer, .NET Dependencies
  F5 / F7 / F8   run / pause / stop

  If a patch produces nothing at all -- every output pin blank rather than wrong -- suspect
  evaluation before logic. Put an IOBox on a node that cannot fail, and relaunch with --log
  to get %UserProfile%\Documents\vvvv\gamma\vvvv_<timestamp>.log. An empty log file is
  itself evidence that nothing ran. See docs\VL-PACKAGING.md.
"@ -ForegroundColor Yellow

& $VvvvPath @vvvvArgs
