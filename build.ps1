<#
.SYNOPSIS
    Builds VL.GIS and stages it as a vvvv package under dist\.

.DESCRIPTION
    Produces this layout:

        dist\VL.GIS\
          VL.GIS.vl              <- entry point (nodes appear in the NodeBrowser)
          VL.GIS.nuspec          <- required for vvvv to recognise a source package
          lib\net8.0\*.dll|.xml
          help\**

    That is byte-for-byte the same shape a published package has once installed under
    %LOCALAPPDATA%\vvvv\gamma\nugets\VL.GIS.<version>\ -- so "works locally but not once
    published" cannot happen.

    dist\ is the package *repository*; dist\VL.GIS\ is the package. Point vvvv at the
    repository, not at the package:

        vvvv.exe --package-repositories D:\2026_Projects\vvvv-gis\dist

    Which DLLs get staged is driven by the <PlatformDependency> entries in VL.GIS.vl,
    so dist\ always contains exactly what the .vl declares -- no drift.
#>
param(
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$Dist     = Join-Path $RepoRoot 'dist'
$PkgDir   = Join-Path $Dist 'VL.GIS'
$VlFile   = Join-Path $RepoRoot 'VL.GIS.vl'

# A running vvvv holds dist\VL.GIS\lib\**\*.dll open, so the restage below fails with a
# confusing "used by another process". Say so plainly instead.
$running = @(Get-Process 'vvvv' -ErrorAction SilentlyContinue)
if ($running -and (Test-Path $Dist)) {
    throw @"
vvvv is running (PID $($running.Id -join ', ')) and is holding the staged assemblies open.

Close vvvv, then run .\build.ps1 again.

Note that a running vvvv keeps the DLLs it loaded at startup -- rebuilding while it is
open would not update the nodes anyway. For an edit-save-see-it loop that does not need
restarting, use .\test\dev.ps1 instead.
"@
}

Write-Host "== 1/4 build ==" -ForegroundColor Cyan
dotnet build (Join-Path $RepoRoot 'VL.GIS.sln') -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

Write-Host "`n== 2/4 read forwarded assemblies from VL.GIS.vl ==" -ForegroundColor Cyan
[xml]$vl = Get-Content $VlFile -Raw
$forwards = @($vl.Document.PlatformDependency | Where-Object { $_.Location -like './lib/*' })
if ($forwards.Count -eq 0) { throw "VL.GIS.vl declares no ./lib/... PlatformDependency" }
$forwards | ForEach-Object { Write-Host "   $($_.Location)" }

Write-Host "`n== 3/4 stage dist\VL.GIS ==" -ForegroundColor Cyan
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PkgDir | Out-Null

Copy-Item $VlFile                              -Destination $PkgDir
Copy-Item (Join-Path $RepoRoot 'VL.GIS.nuspec') -Destination $PkgDir

foreach ($fwd in $forwards) {
    # './lib/net8.0/VL.GIS.Core.dll' -> assembly name + target subfolder
    $rel        = $fwd.Location -replace '^\./', ''
    $asmName    = [IO.Path]::GetFileNameWithoutExtension($rel)
    $targetDir  = Join-Path $PkgDir (Split-Path $rel -Parent)
    $sourceDir  = Join-Path $RepoRoot "src\$asmName\bin\$Configuration\net8.0"

    if (-not (Test-Path $sourceDir)) { throw "Build output not found: $sourceDir" }
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

    foreach ($ext in 'dll', 'xml') {
        $src = Join-Path $sourceDir "$asmName.$ext"
        if (Test-Path $src) { Copy-Item $src -Destination $targetDir }
        elseif ($ext -eq 'dll') { throw "Missing $src" }
    }
    Write-Host "   staged $asmName -> $((Resolve-Path $targetDir).Path.Replace($RepoRoot, '.'))"
}

$helpSrc = Join-Path $RepoRoot 'help'
if ((Test-Path $helpSrc) -and (Get-ChildItem $helpSrc -File -Recurse -ErrorAction SilentlyContinue)) {
    # Normalise the repo copy, not the staged one. VL.GIS.nuspec packs help patches
    # straight out of help\ -- dist\ is not involved at release time -- so a fix applied
    # on the way into dist\ would be invisible in the published package.
    & (Join-Path $RepoRoot 'tools\Normalize-HelpPatches.ps1')

    Copy-Item $helpSrc -Destination $PkgDir -Recurse
    Write-Host "   staged help\"
}

Write-Host "`n== 4/4 done ==" -ForegroundColor Green
Get-ChildItem $PkgDir -Recurse -File |
    ForEach-Object { "   " + $_.FullName.Replace("$PkgDir\", '') + "  [$($_.Length) B]" }

Write-Host @"

Next:
  .\test\verify.ps1                     headless check (fast, exits non-zero on failure)
  .\test\test.ps1                       launch vvvv against dist\
"@ -ForegroundColor Yellow
