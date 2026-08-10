<#
.SYNOPSIS
    Hot-reload loop for writing C# nodes: edit a .cs file, save, see it live in vvvv.

.DESCRIPTION
    Opens test\DevLoop.vl, a scratch document that references src\VL.GIS.Core\*.csproj
    via <ProjectDependency> rather than the built .dll. Saving any .cs file then triggers
    compilation and hotswaps the running code -- no rebuild, no vvvv restart.

    This is the fast loop for *writing nodes*. It is NOT a package test: it bypasses
    VL.GIS.vl, the nuspec and dist\ entirely. Before committing, always run

        .\build.ps1 ; .\test\verify.ps1

    to check the thing you actually ship.

    Caveats from the vvvv docs:
      - Static methods hotswap cleanly. Stateful instances lose their state on every
        save; classes holding unmanaged resources need a full vvvv restart.
      - Never put a <ProjectDependency> in VL.GIS.vl itself. It would force VL.GIS and
        everything depending on it to stay editable, costing startup time and memory.
        That is why this lives in a separate scratch document.

    To debug with breakpoints: attach Visual Studio to vvvv.exe, and turn off
    Debug > Options > "Require source files to exactly match the original version"
    (hotswapped assemblies no longer match on disk).

.EXAMPLE
    .\test\dev.ps1
#>
param(
    [string]$VvvvPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$DevDoc   = Join-Path $PSScriptRoot 'DevLoop.vl'

if (-not (Test-Path $DevDoc)) {
    throw "$DevDoc not found. Regenerate it with:
  .\tools\New-VLDocument.ps1 -OutFile .\test\DevLoop.vl -DefaultCategory DevLoop ``
      -NugetDependency ([ordered]@{ 'VL.CoreLib' = '2025.7.0' }) ``
      -ProjectReference '../src/VL.GIS.Core/VL.GIS.Core.csproj'"
}

if (-not $VvvvPath) {
    $VvvvPath = & (Join-Path $RepoRoot 'tools\Find-Vvvv.ps1')
}
if (-not (Test-Path $VvvvPath)) {
    throw "vvvv.exe not found at '$VvvvPath'. Pass -VvvvPath explicitly."
}

Write-Host "vvvv     : $VvvvPath"
Write-Host "document : $DevDoc"
Write-Host @"

Hot-reload loop:
  1. vvvv opens DevLoop.vl
  2. Double left-click the canvas, search for a node (e.g. GisVersion)
  3. Edit src\VL.GIS.Core\*.cs in your editor and save
  4. The node updates live -- no rebuild, no restart

  Ctrl+Shift+F2  Log window
  Ctrl+U         Solution Explorer, .NET Dependencies (check the csproj is referenced)
"@ -ForegroundColor Yellow

& $VvvvPath --open $DevDoc
