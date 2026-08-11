<#
.SYNOPSIS
    Builds VL.GIS, then opens vvvv with it loaded. The one command for "start working".

.DESCRIPTION
    VL.GIS is never installed into %LOCALAPPDATA%\vvvv\gamma\nugets. It is read straight off
    disk from dist\, which only happens when vvvv is launched with

        --package-repositories <repo>\dist

    so starting vvvv from the Start menu leaves the library invisible, with no error to
    explain why. That is the single most common way to lose time on this repository, and
    avoiding it is most of what this script is for.

    Note the nesting: the argument is the *repository* (dist\), which holds one folder per
    package (dist\VL.GIS\). Passing the package folder finds nothing and says nothing.

    What it does, in order:

      1. deals with any vvvv already running (see below)
      2. runs build.ps1, so the nodes in the patch match the source you last edited
      3. offers a menu of documents to open, or takes a name fragment as an argument
      4. launches vvvv with the repository and --log

    A document is opened rather than a blank patch because a package sitting in a repository
    is *available*, not *referenced*. A blank patch does not depend on VL.GIS, so its nodes
    stay out of the NodeBrowser -- and searching for one there offers to install VL.GIS from
    nuget.org instead, which is a different, older, broken package.

.PARAMETER Match
    Case-insensitive fragment of a document name, to skip the menu: .\start.ps1 tile

.EXAMPLE
    .\start.ps1
.EXAMPLE
    .\start.ps1 tile
.EXAMPLE
    .\start.ps1 -Restart -NoBuild
#>
param(
    # Name fragment to pick a document without the menu. Ambiguous matches narrow the menu
    # rather than guessing.
    [Parameter(Position = 0)]
    [string]$Match = '',

    # Skip the build. Only worth it when you know dist\ is current.
    [switch]$NoBuild,

    # Start from an empty patch. You then have to add the VL.GIS dependency yourself via
    # Ctrl+J > Dependencies, or its nodes will not appear.
    [switch]$Blank,

    # Load VL.GIS from source so the patches inside it stay editable; packages are read-only
    # by default.
    [switch]$Editable,

    # Kill a running vvvv even when it still has a window. Loses unsaved work.
    [switch]$Restart,

    [string]$VvvvPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$Dist     = Join-Path $RepoRoot 'dist'

# ---------------------------------------------------------------- running vvvv
# vvvv holds dist\VL.GIS\lib\**\*.dll open, so build.ps1 refuses to run while it is up. It
# also, in practice, sometimes keeps its process alive after the window is gone -- that
# happened twice in one evening -- which leaves the repository unbuildable until someone
# notices and kills it by hand.
#
# The heuristic: a process with no main window has nothing on screen to lose, so ending it
# is safe. One with a window might be holding unsaved patches, so refuse and say so.
#
# This has NOT been confirmed against an actually-wedged vvvv; it is verified only that
# MainWindowHandle discriminates in general (explorer reports a handle, svchost reports 0).
# The bias is therefore towards refusing: if a wedged vvvv does keep its handle, this script
# declines to start and you kill it yourself, which is exactly the status quo. It will not
# take the other kind of mistake, which costs work.
$running = @(Get-Process 'vvvv' -ErrorAction SilentlyContinue)

foreach ($proc in $running) {
    $hasWindow = $proc.MainWindowHandle -ne 0

    if ($Restart) {
        Write-Host "ending vvvv (PID $($proc.Id))" -ForegroundColor Yellow
        Stop-Process -Id $proc.Id -Force
        continue
    }

    if ($hasWindow) {
        throw @"
vvvv is already running (PID $($proc.Id)) and still has a window.

Save your work and close it, then run .\start.ps1 again.
Or pass -Restart to end it regardless -- unsaved patches are lost.
"@
    }

    Write-Host "found a vvvv process with no window (PID $($proc.Id)) -- ending it" -ForegroundColor Yellow
    Write-Host "  vvvv sometimes outlives its window; it would have blocked the build." -ForegroundColor DarkGray
    Stop-Process -Id $proc.Id -Force
}

if ($running) { Start-Sleep -Milliseconds 800 }

# ---------------------------------------------------------------- build
# After the kill, never before: build.ps1 checks for a running vvvv and throws.
if (-not $NoBuild) {
    & (Join-Path $RepoRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed" }
    Write-Host ''
}

if (-not (Test-Path (Join-Path $Dist 'VL.GIS\VL.GIS.vl'))) {
    throw "dist\VL.GIS\VL.GIS.vl not found. Run .\start.ps1 without -NoBuild."
}

# ---------------------------------------------------------------- pick a document
# Scanned, not hardcoded, so a new help patch shows up here by existing.
$documents = @()
$smoke = Join-Path $RepoRoot 'test\SmokeTest.vl'
if (Test-Path $smoke) {
    $documents += [pscustomobject]@{ Name = 'SmokeTest'; Where = 'test\'; Path = $smoke }
}
Get-ChildItem (Join-Path $RepoRoot 'help') -Filter *.vl -ErrorAction SilentlyContinue |
    Sort-Object Name |
    ForEach-Object {
        $documents += [pscustomobject]@{
            Name  = $_.BaseName
            Where = 'help\'
            Path  = $_.FullName
        }
    }

$doc = $null

if (-not $Blank) {
    if ($documents.Count -eq 0) { throw "No .vl documents found in help\ or test\." }

    $candidates = @($documents)
    if ($Match) {
        $candidates = @($documents | Where-Object { $_.Name -like "*$Match*" })
        if ($candidates.Count -eq 0) {
            throw "Nothing matches '$Match'. Run .\start.ps1 with no arguments for the menu."
        }
    }

    if ($candidates.Count -eq 1 -and $Match) {
        $doc = $candidates[0]
    }
    else {
        $width = ($candidates | ForEach-Object { $_.Name.Length } | Measure-Object -Maximum).Maximum

        Write-Host ''
        for ($i = 0; $i -lt $candidates.Count; $i++) {
            "  {0}  {1}  {2}" -f ($i + 1), $candidates[$i].Name.PadRight($width), $candidates[$i].Where |
                Write-Host
        }
        $blankIndex = $candidates.Count + 1
        "  {0}  {1}  (you must add the VL.GIS dependency yourself)" -f $blankIndex, '(blank patch)'.PadRight($width) |
            Write-Host
        Write-Host ''

        # Read-Host throws outright when the host has no console to prompt with, which is
        # how this script behaves under automation or a redirected stdin. Falling back to
        # the default beats an exception, and it is what pressing Enter would have done.
        try {
            $choice = Read-Host "open [1-$blankIndex], Enter for 1"
        }
        catch {
            Write-Host "  (not interactive -- taking 1)" -ForegroundColor DarkGray
            $choice = '1'
        }
        if ([string]::IsNullOrWhiteSpace($choice)) { $choice = '1' }

        $index = 0
        if (-not [int]::TryParse($choice, [ref]$index) -or $index -lt 1 -or $index -gt $blankIndex) {
            throw "'$choice' is not one of 1-$blankIndex."
        }

        if ($index -eq $blankIndex) { $Blank = $true } else { $doc = $candidates[$index - 1] }
    }
}

# ---------------------------------------------------------------- launch
if (-not $VvvvPath) { $VvvvPath = & (Join-Path $RepoRoot 'tools\Find-Vvvv.ps1') }
if (-not (Test-Path $VvvvPath)) { throw "vvvv.exe not found at '$VvvvPath'. Pass -VvvvPath." }

# --log is always on. Its absence cost an evening: with no log there was no way to tell a
# patch that failed from one that never ran, and an empty log file turns out to be strong
# evidence of the latter.
$vvvvArgs = @('--package-repositories', $Dist, '--log')
if ($Editable) { $vvvvArgs += @('--editable-packages', 'VL.GIS') }
if ($doc)      { $vvvvArgs += @('--open', $doc.Path) }

Write-Host ''
Write-Host "vvvv       : $VvvvPath"
Write-Host "repository : $Dist"
Write-Host "document   : $(if ($doc) { $doc.Name } else { '(blank patch)' })"
Write-Host "log        : $env:UserProfile\Documents\vvvv\gamma\vvvv_<timestamp>.log"
Write-Host @"

In vvvv:
  Double left-click empty canvas   NodeBrowser; try CreatePoint or GisVersion
  Drag from an output pin, then Alt+left-click empty canvas   IOBox showing the value

  Do NOT accept a NodeBrowser offer to download or install "VL.GIS". That is the older
  package on nuget.org; the one under test is already loaded from dist\.

  Ctrl+Shift+F2  Log window          Ctrl+J  Solution Explorer (Dependencies)
  F5 / F7 / F8   run / pause / stop  Ctrl+U  .NET Dependencies

  If a patch produces nothing at all -- every output pin blank rather than wrong -- suspect
  evaluation before logic. Put an IOBox on a node that cannot fail, and read the log above.
  An empty log file is itself evidence that nothing ran. See docs\VL-PACKAGING.md.

  Edited a .vl here? Run .\build.ps1 (or just .\start.ps1 again) before committing: it pins
  each help patch's VL.GIS dependency back to 0.0.0, which vvvv rewrites on every save.
"@ -ForegroundColor Yellow

& $VvvvPath @vvvvArgs
