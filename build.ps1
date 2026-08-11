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

Write-Host "== 1/4 build ==" -ForegroundColor Cyan
dotnet build (Join-Path $RepoRoot 'VL.GIS.sln') -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

Write-Host "`n== 2/4 stage dist\ ==" -ForegroundColor Cyan
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

    # Help patches belong to whichever package's nuspec ships them, which is decided there
    # rather than here.
    [xml]$nuspec = Get-Content $pkg.Nuspec -Raw
    $shipsHelp = @($nuspec.package.files.file | Where-Object { $_.src -like 'help\*' }).Count -gt 0
    $helpSrc = Join-Path $RepoRoot 'help'
    if ($shipsHelp -and (Test-Path $helpSrc) -and (Get-ChildItem $helpSrc -File -Recurse -ErrorAction SilentlyContinue)) {
        Copy-Item $helpSrc -Destination $pkg.PkgDir -Recurse
        Write-Host "      help\"
    }
}

Write-Host "`n== 3/4 staged ==" -ForegroundColor Cyan
foreach ($pkg in $Packages) {
    Get-ChildItem $pkg.PkgDir -Recurse -File |
        ForEach-Object { "   " + $_.FullName.Replace("$Dist\", '') + "  [$($_.Length) B]" }
}

Write-Host "`n== 4/4 done ==" -ForegroundColor Green

Write-Host @"

Next:
  .\test\verify.ps1                     headless check (fast, exits non-zero on failure)
  .\start.ps1                           launch vvvv against dist\ and pick a patch
"@ -ForegroundColor Yellow
