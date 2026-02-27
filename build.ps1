# VL.GIS build + pack script (Windows PowerShell)
# Run from the repo root: .\build.ps1
# Optional: .\build.ps1 -Configuration Release

param(
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "=== VL.GIS Build ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"

# Restore
Write-Host "`n-- Restoring packages..." -ForegroundColor Yellow
dotnet restore VL.GIS.sln
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

# Build
Write-Host "`n-- Building solution..." -ForegroundColor Yellow
dotnet build VL.GIS.sln -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

# Pack
Write-Host "`n-- Packing NuGet..." -ForegroundColor Yellow
dotnet pack src/VL.GIS.Core/VL.GIS.Core.csproj -c $Configuration --no-build -o nupkg/
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed" }

$pkg = Get-ChildItem nupkg/*.nupkg | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "`n=== Done ===" -ForegroundColor Green
Write-Host "Package: $($pkg.FullName)"
Write-Host ""
Write-Host "--- Import Path A (dev, direct DLL) ---"
Write-Host "In vvvv: Quad menu > Edit > Import .NET Assembly"
Write-Host "Browse to: src\VL.GIS.Core\bin\$Configuration\net8.0\VL.GIS.Core.dll"
Write-Host "Repeat for VL.GIS.Tiles.dll and VL.GIS.Stride.dll"
Write-Host ""
Write-Host "--- Import Path B (NuGet feed) ---"
Write-Host "In vvvv: Quad menu > Edit > Manage NuGet Packages > gear icon > Add package source"
Write-Host "Point to: $(Resolve-Path nupkg/)"
Write-Host "Search for 'VL.GIS' and install."
