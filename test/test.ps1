<#
.SYNOPSIS
  Fast local test loop for VL.GIS. Builds, stages DLLs to lib/net8.0/, and
  launches vvvv with the package loaded directly from the repo — no pack/install needed.

.USAGE
  .\test\test.ps1
  .\test\test.ps1 -VvvvPath "C:\custom\path\to\vvvv.exe"
#>
param(
    [string]$VvvvPath = ""
)

$RepoRoot = Split-Path $PSScriptRoot -Parent

# 1. Build
Write-Host "Building VL.GIS..." -ForegroundColor Cyan
dotnet build "$RepoRoot\VL.GIS.sln" -c Release
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }

# 2. Verify DLLs
$required = @(
    "src\VL.GIS.Core\bin\Release\net8.0\VL.GIS.Core.dll",
    "src\VL.GIS.Tiles\bin\Release\net8.0\VL.GIS.Tiles.dll",
    "src\VL.GIS.Stride\bin\Release\net8.0\VL.GIS.Stride.dll"
)
foreach ($rel in $required) {
    $full = Join-Path $RepoRoot $rel
    if (-not (Test-Path $full)) { Write-Error "Missing DLL: $rel"; exit 1 }
    Write-Host "OK: $rel" -ForegroundColor Green
}

# 2.5 Stage DLLs to lib/net8.0/ for --package-repositories mode
# VL.GIS.vl references ./lib/net8.0/*.dll relative to the repo root,
# so we copy the build outputs there before launching vvvv.
$libDir = Join-Path $RepoRoot "lib\net8.0"
New-Item -ItemType Directory -Force -Path $libDir | Out-Null
foreach ($rel in $required) {
    Copy-Item (Join-Path $RepoRoot $rel) $libDir -Force
    Write-Host "Staged: $(Split-Path $rel -Leaf)" -ForegroundColor Green
}

# 3. Find vvvv.exe
if (-not $VvvvPath) {
    $found = Get-ChildItem "C:\Program Files\vvvv" -Filter "vvvv.exe" -Recurse -ErrorAction SilentlyContinue |
             Select-Object -First 1
    if ($found) { $VvvvPath = $found.FullName }
    else {
        $candidates = @(
            "C:\Program Files\vvvv\vvvv_gamma_7.0\vvvv.exe",
            "$env:LOCALAPPDATA\vvvv\gamma\vvvv.exe"
        )
        foreach ($c in $candidates) {
            if (Test-Path $c) { $VvvvPath = $c; break }
        }
    }
}
if (-not $VvvvPath -or -not (Test-Path $VvvvPath)) {
    Write-Error "Cannot find vvvv.exe. Pass -VvvvPath 'C:\path\to\vvvv.exe'"
    exit 1
}

# 4. Launch vvvv with local package repository
Write-Host ""
Write-Host "Launching vvvv with VL.GIS from: $RepoRoot" -ForegroundColor Cyan
Write-Host "NodeBrowser: search 'GIS' or 'CreatePoint' to verify nodes." -ForegroundColor Yellow
Write-Host "Log: Ctrl+Shift+L — look for red errors on VL.GIS." -ForegroundColor Yellow
Write-Host ""
& $VvvvPath --package-repositories $RepoRoot
