<#
.SYNOPSIS
    Headless check that the VL.GIS package loads and can be consumed.

.DESCRIPTION
    Stage 1 (always) -- compile dist\VL.GIS\VL.GIS.vl with vvvv's commandline compiler.
    Exercises the whole chain without opening the GUI:

      - VL.GIS.vl deserializes (document IDs, BOM, <Patch> structure)
      - every <NugetDependency> resolves
      - every forwarded assembly in lib\net8.0 loads and can be reflected over
      - the result compiles to a runnable .exe

    Stage 2 (-EndToEnd) -- pack a .nupkg into dist\feed, then compile test\SmokeTest.vl,
    a *separate* document whose only content is a dependency on VL.GIS. This is the real
    question ("can someone else consume this package?") and it caught nothing that
    stage 1 caught -- the two fail in different ways.

    Stage 2 needs vvvv gamma >= 7.1; --export-package-sources was broken before that.

    vvvvc fails on VL compile errors ("red nodes") unless --ignore-errors is passed, so
    exit code 0 means the package is genuinely sound.

    What neither stage proves: that a node shows up in the NodeBrowser under the expected
    category. Confirm that once in the GUI via .\test\test.ps1.

    Run .\build.ps1 first.

.EXAMPLE
    .\test\verify.ps1
.EXAMPLE
    .\test\verify.ps1 -EndToEnd
#>
param(
    [string]$VvvvcPath = '',
    # Also pack and consume the package from a separate document. Slower, stronger.
    [switch]$EndToEnd
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$PkgVl    = Join-Path $RepoRoot 'dist\VL.GIS\VL.GIS.vl'

if (-not (Test-Path $PkgVl)) {
    throw "$PkgVl not found. Run .\build.ps1 first."
}

if (-not $VvvvcPath) {
    $VvvvcPath = & (Join-Path $RepoRoot 'tools\Find-Vvvv.ps1') -Compiler
}
if (-not (Test-Path $VvvvcPath)) {
    throw "vvvvc.exe not found at '$VvvvcPath'. Pass -VvvvcPath explicitly."
}

# vvvvc rejects relative paths outright ("The file path must be absolute").
$PkgVl = (Resolve-Path $PkgVl).Path
$Dist  = (Resolve-Path (Join-Path $RepoRoot 'dist')).Path

Write-Host "compiler : $VvvvcPath"

# ---------------------------------------------------------------- stage 0
# Cheap, and catches the one failure that every later stage reports as success.
Write-Host "`n== stage 0: forwarded assemblies opt in to VL import ==" -ForegroundColor Cyan

[xml]$vlDoc = Get-Content $PkgVl -Raw
$forwarded = @($vlDoc.Document.PlatformDependency | Where-Object { $_.Location -like './lib/*' })
$noImport = @()

foreach ($fwd in $forwarded) {
    $dll  = Join-Path (Split-Path $PkgVl -Parent) ($fwd.Location -replace '^\./', '')
    $name = Split-Path $dll -Leaf
    $found = @(& (Join-Path $RepoRoot 'tools\Test-VLImportAttribute.ps1') -Path $dll)
    if ($found) { Write-Host "   $name -> $($found -join ', ')" }
    else        { Write-Host "   $name -> none" -ForegroundColor Red; $noImport += $name }
}

if ($noImport) {
    Write-Host @"

FAIL - no VL.Core.Import attribute in: $($noImport -join ', ')

  These assemblies will forward without error, but their public statics will not appear
  as nodes in the NodeBrowser. Add to the project:

      <PackageReference Include="VL.Core" Version="2025.7.0" />

  and to a .cs file:

      using VL.Core.Import;
      [assembly: ImportAsIs(Namespace = "VL")]
"@ -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------- stage 1
Write-Host "`n== stage 1: package document compiles ==" -ForegroundColor Cyan
Write-Host "document : $PkgVl`n"

$out1 = Join-Path $Dist '_verify'
& $VvvvcPath $PkgVl --output-directory $out1 -v Information
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nFAIL - vvvvc exited with $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

# The exported app must carry the forwarded assemblies; if a PlatformDependency silently
# failed to resolve, the .exe would still build but the .dll would be absent.
[xml]$vl = Get-Content $PkgVl -Raw
$expected = @($vl.Document.PlatformDependency |
    Where-Object { $_.Location -like './lib/*' } |
    ForEach-Object { Split-Path $_.Location -Leaf })

$missing = $expected | Where-Object { -not (Test-Path (Join-Path $out1 "VL.GIS\$_")) }
if ($missing) {
    Write-Host "`nFAIL - forwarded assemblies missing from export: $($missing -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "`nstage 1 PASS - forwarded: $($expected -join ', ')" -ForegroundColor Green

if (-not $EndToEnd) {
    Write-Host "`nPASS (stage 1 only; add -EndToEnd for the consumer test)" -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------- stage 2
Write-Host "`n== stage 2: a separate document consumes the packed nupkg ==" -ForegroundColor Cyan

& (Join-Path $RepoRoot 'pack.ps1') -NoBuild
if ($LASTEXITCODE -ne 0) { throw "pack.ps1 failed" }

$smoke = (Resolve-Path (Join-Path $PSScriptRoot 'SmokeTest.vl')).Path
$feed  = (Resolve-Path (Join-Path $Dist 'feed')).Path
$out2  = Join-Path $Dist '_e2e'

Write-Host "`ndocument : $smoke"
Write-Host "feed     : $feed`n"

& $VvvvcPath $smoke --package-repositories $Dist --export-package-sources $feed `
    --output-directory $out2 -v Warning
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nFAIL - consumer document failed to build ($LASTEXITCODE)" -ForegroundColor Red
    Write-Host "       On gamma < 7.1 this is expected: --export-package-sources was broken." -ForegroundColor Yellow
    exit $LASTEXITCODE
}

$consumerMissing = $expected | Where-Object { -not (Test-Path (Join-Path $out2 "SmokeTest\$_")) }
if ($consumerMissing) {
    Write-Host "`nFAIL - consumer export missing: $($consumerMissing -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "`nPASS - package builds, packs, and is consumable by another document." -ForegroundColor Green
exit 0
