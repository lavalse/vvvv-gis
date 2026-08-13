<#
.SYNOPSIS
    Builds every package in this repository and stages them under dist\.

.DESCRIPTION
    A package is a .vl at the repo root with a .nuspec of the same name beside it. They are
    discovered rather than listed, so adding one needs no edit here.

        dist\VL.GIS\
          VL.GIS.vl              <- entry point (nodes appear in the NodeBrowser)
          VL.GIS.nuspec          <- required for vvvv to recognise a source package
          lib\net8.0\*.dll|.xml
          help\**
        dist\VL.GIS.Skia\
          ...

    That is byte-for-byte the same shape a published package has once installed under
    %LOCALAPPDATA%\vvvv\gamma\nugets\<id>.<version>\ -- so "works locally but not once
    published" cannot happen.

    dist\ is the package *repository*; each folder inside it is a package. Point vvvv at the
    repository, not at a package:

        vvvv.exe --package-repositories D:\2026_Projects\vvvv-gis\dist

    Which DLLs get staged is driven by each .vl's <PlatformDependency> entries, so dist\
    always contains exactly what the documents declare -- no drift.
#>
param(
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$Dist     = Join-Path $RepoRoot 'dist'
$Deps     = Join-Path $RepoRoot 'deps'   # upstream packages, kept apart from ours - see step 3

# A package is a .vl at the repo root with a .nuspec of the same name beside it. Discovered
# rather than listed, so adding VL.GIS.<Something> needs no edit here.
$Packages = @(
    Get-ChildItem $RepoRoot -Filter '*.vl' -File |
        Where-Object { Test-Path (Join-Path $RepoRoot "$($_.BaseName).nuspec") } |
        Sort-Object BaseName |
        ForEach-Object {
            [pscustomobject]@{
                Name   = $_.BaseName
                VlFile = $_.FullName
                Nuspec = Join-Path $RepoRoot "$($_.BaseName).nuspec"
                PkgDir = Join-Path $Dist $_.BaseName
            }
        }
)
if ($Packages.Count -eq 0) { throw "No package found: expected a .vl with a matching .nuspec at $RepoRoot" }

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

Write-Host "== 1/5 build ==" -ForegroundColor Cyan
dotnet build (Join-Path $RepoRoot 'VL.GIS.sln') -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

Write-Host "`n== 2/5 stage dist\ ==" -ForegroundColor Cyan
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }

# Normalise the repo copy of the help patches, not the staged one. The nuspec packs
# help\**\*.vl straight out of the repo -- dist\ is not involved at release time -- so a fix
# applied on the way into dist\ would be invisible in the published package.
& (Join-Path $RepoRoot 'tools\Normalize-HelpPatches.ps1')

foreach ($pkg in $Packages) {
    Write-Host "`n   $($pkg.Name)" -ForegroundColor White
    New-Item -ItemType Directory -Force -Path $pkg.PkgDir | Out-Null

    Copy-Item $pkg.VlFile -Destination $pkg.PkgDir
    Copy-Item $pkg.Nuspec -Destination $pkg.PkgDir

    [xml]$vl = Get-Content $pkg.VlFile -Raw
    $forwards = @($vl.Document.PlatformDependency | Where-Object { $_.Location -like './lib/*' })
    if ($forwards.Count -eq 0) { throw "$($pkg.Name).vl declares no ./lib/... PlatformDependency" }

    foreach ($fwd in $forwards) {
        # './lib/net8.0/VL.GIS.Core.dll' -> assembly name + target subfolder
        $rel        = $fwd.Location -replace '^\./', ''
        $asmName    = [IO.Path]::GetFileNameWithoutExtension($rel)
        $targetDir  = Join-Path $pkg.PkgDir (Split-Path $rel -Parent)
        $sourceDir  = Join-Path $RepoRoot "src\$asmName\bin\$Configuration\net8.0"

        if (-not (Test-Path $sourceDir)) { throw "Build output not found: $sourceDir" }
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

        foreach ($ext in 'dll', 'xml') {
            $src = Join-Path $sourceDir "$asmName.$ext"
            if (Test-Path $src) { Copy-Item $src -Destination $targetDir }
            elseif ($ext -eq 'dll') { throw "Missing $src" }
        }
        Write-Host "      forwards $asmName"
    }

    # Help patches live in help\<PackageName>\ and are staged as help\, which is where vvvv
    # looks for them. Splitting them by package keeps a patch that needs VL.GIS.Skia out of
    # VL.GIS, where it would open with a missing dependency. The nuspec decides whether a
    # package ships any; this only has to agree with it.
    [xml]$nuspec = Get-Content $pkg.Nuspec -Raw
    $shipsHelp = @($nuspec.package.files.file | Where-Object { $_.src -like 'help\*' }).Count -gt 0
    $helpSrc = Join-Path $RepoRoot "help\$($pkg.Name)"
    if ($shipsHelp -and (Test-Path $helpSrc) -and (Get-ChildItem $helpSrc -File -Recurse -ErrorAction SilentlyContinue)) {
        Copy-Item $helpSrc -Destination (Join-Path $pkg.PkgDir 'help') -Recurse
        Write-Host "      help\ (from help\$($pkg.Name))"
    }
}

Write-Host "`n== 3/5 upstream packages ==" -ForegroundColor Cyan
#
# The upstream libraries have to sit in the package repository as packages, not merely be
# restorable as assemblies. Without that, VL cannot resolve their types, and the failure is the
# quiet kind: a node whose signature mentions BruTile's IHttpTileSource is built but none of its
# links attach, so it vanishes from the compiled program and whatever consumed it silently
# receives a default.
#
# This used to work by accident. Installing VL.GIS from nuget.org had put BruTile, ProjNet and
# NetTopologySuite into %LOCALAPPDATA%\vvvv\gamma\nugets\ back in February, and everything here
# quietly resolved through that shared folder. Moving BruTile out of it on 2026-08-13 - to stop
# it shadowing the BruTile 5 that VL.Mapsui needs - broke this repository until this step
# existed. A dist\ that carries its own upstream packages does not depend on what some other
# project happens to have installed machine-wide, and the two repositories stop fighting.
#
# Discovered from each .vl rather than listed: anything not starting with VL. is an upstream
# library, since the VL.* ones ship inside vvvv. Transitive dependencies come too, because a
# real install gets them.
$NuGetExe = & (Join-Path $RepoRoot 'tools\Find-Vvvv.ps1') -NuGet
foreach ($pkg in $Packages) {
    [xml]$vlDoc = Get-Content $pkg.VlFile -Raw
    foreach ($dep in @($vlDoc.Document.NugetDependency | Where-Object { $_.Location -notlike 'VL.*' })) {
        $folder = Join-Path $Deps "$($dep.Location).$($dep.Version)"
        if (Test-Path $folder) { continue }

        & $NuGetExe install $dep.Location -Version $dep.Version -OutputDirectory $Deps `
            -Source 'https://api.nuget.org/v3/index.json' -NonInteractive | Out-Null
        if (-not (Test-Path $folder)) { throw "Could not install $($dep.Location) $($dep.Version) into $Deps" }
        Write-Host "      $($dep.Location) $($dep.Version)"
    }
}

Write-Host "`n== 4/5 staged ==" -ForegroundColor Cyan
foreach ($pkg in $Packages) {
    Get-ChildItem $pkg.PkgDir -Recurse -File |
        ForEach-Object { "   " + $_.FullName.Replace("$Dist\", '') + "  [$($_.Length) B]" }
}

Write-Host "`n== 5/5 done ==" -ForegroundColor Green

Write-Host @"

Next:
  .\test\verify.ps1                     headless check (fast, exits non-zero on failure)
  .\start.ps1                           launch vvvv against dist\ and pick a patch
"@ -ForegroundColor Yellow
