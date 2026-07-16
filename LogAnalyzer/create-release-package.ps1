# Script to create a redistributable package of AlyCE Log Analyzer (Blazor Server)
# Publishes a self-contained Windows x64 executable that hosts the web app locally.

param(
    [string]$PackageOutputPath = "./dist",
    [switch]$SkipBuild = $false,
    [switch]$SkipClean = $false
)

$ErrorActionPreference = "Stop"
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "AlyCE Log Analyzer (Server) - Release Package Builder" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

# Define paths
$projectPath   = $scriptPath
$projectFile   = Join-Path $projectPath "LogAnalyzer.csproj"
$binPath       = Join-Path $projectPath "bin\Release"
$objPath       = Join-Path $projectPath "obj\Release"
$publishPath   = Join-Path $projectPath "bin\Release\net8.0\win-x64\publish"

if ([System.IO.Path]::IsPathRooted($PackageOutputPath)) {
    $packagePath = $PackageOutputPath
} else {
    $packagePath = Join-Path (Get-Location) $PackageOutputPath
}

$timestamp   = Get-Date -Format "yyyyMMdd_HHmmss"
$packageName = "AlyCE-LogAnalyzer-Server-v1.0_$timestamp"

Write-Host "Project Path:   $projectPath" -ForegroundColor Yellow
Write-Host "Package Output: $packagePath" -ForegroundColor Yellow
Write-Host ""

# Clean previous builds
if (-not $SkipClean) {
    Write-Host "Cleaning previous builds..." -ForegroundColor Cyan
    if (Test-Path $binPath) {
        Remove-Item -Path $binPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  [OK] Cleaned bin/Release" -ForegroundColor Green
    }
    if (Test-Path $objPath) {
        Remove-Item -Path $objPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  [OK] Cleaned obj/Release" -ForegroundColor Green
    }
}

# Build
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "Building project in Release mode..." -ForegroundColor Cyan

    & dotnet build $projectFile -c Release -f net8.0 --runtime win-x64 --self-contained

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "  [OK] Build completed successfully" -ForegroundColor Green
}

# Publish
Write-Host ""
Write-Host "Publishing application..." -ForegroundColor Cyan

& dotnet publish $projectFile `
    -c Release `
    -f net8.0 `
    --runtime win-x64 `
    --self-contained `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:SelfContained=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  [OK] Publish completed successfully" -ForegroundColor Green

# Create output directory
if (-not (Test-Path $packagePath)) {
    New-Item -ItemType Directory -Path $packagePath | Out-Null
    Write-Host "  [OK] Created output directory: $packagePath" -ForegroundColor Green
}

# Create staging directory
Write-Host ""
Write-Host "Creating redistributable package..." -ForegroundColor Cyan

$packageDir = Join-Path $packagePath $packageName
$appDir     = Join-Path $packageDir "Application"
New-Item -ItemType Directory -Path $appDir | Out-Null

# Copy published files
Write-Host "  Copying application files..." -ForegroundColor Yellow
Copy-Item -Path "$publishPath\*" -Destination $appDir -Recurse -Force

# Create a launcher batch file
$launchBat = Join-Path $packageDir "Launch.bat"
Set-Content -Path $launchBat -Value @"
@echo off
echo Starting AlyCE Log Analyzer...
echo.
echo The app will open in your browser at http://localhost:5134
echo Close this window to stop the server.
echo.
start "" http://localhost:5134
"%~dp0Application\LogAnalyzer.exe"
"@ -Encoding ASCII
Write-Host "  [OK] Created Launch.bat" -ForegroundColor Green

# Copy README from repo root
$readmeSourcePath = Join-Path (Split-Path -Parent $projectPath) "README-HOW-TO.txt"
if (Test-Path $readmeSourcePath) {
    Copy-Item -Path $readmeSourcePath -Destination (Join-Path $packageDir "README.txt")
    Write-Host "  [OK] Added README.txt" -ForegroundColor Green
} else {
    Write-Host "  Warning: README-HOW-TO.txt not found at $readmeSourcePath" -ForegroundColor Yellow
}

# Create ZIP archive
Write-Host ""
Write-Host "Creating ZIP archive..." -ForegroundColor Cyan

$zipPath = Join-Path $packagePath "$packageName.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($packageDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "  [OK] Created ZIP archive: $(Split-Path -Leaf $zipPath)" -ForegroundColor Green

$zipSize = (Get-Item $zipPath).Length / 1MB
Write-Host ("  [OK] Package size: {0:F2} MB" -f $zipSize) -ForegroundColor Green

# Create manifest
$manifestContent = @"
Package Name: $packageName
Creation Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Application: AlyCE Log Analyzer (Blazor Server)
Version: 1.0
Target Platform: Windows (x64)
Runtime: .NET 8.0 (Self-contained)
Size: {0:F2} MB

Contents:
- Fully self-contained application (no .NET runtime required)
- Launch.bat  ? double-click to start the server and open the browser
- README.txt  ? usage instructions

Usage:
  1. Extract the ZIP anywhere on the target machine
  2. Double-click Launch.bat (or run Application\LogAnalyzer.exe directly)
  3. The browser opens automatically at http://localhost:5134
  4. Close the console window to stop the server
"@ -f $zipSize

$manifestPath = Join-Path $packagePath "$packageName.manifest"
Set-Content -Path $manifestPath -Value $manifestContent
Write-Host "  [OK] Created manifest file" -ForegroundColor Green

# Cleanup staging directory after ZIP is created
Remove-Item -Path $packageDir -Recurse -Force

# Cleanup release build artifacts
Write-Host ""
Write-Host "Cleaning up release build artifacts..." -ForegroundColor Cyan
if (Test-Path $binPath) {
    Remove-Item -Path $binPath -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  [OK] Cleaned bin/Release" -ForegroundColor Green
}
if (Test-Path $objPath) {
    Remove-Item -Path $objPath -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  [OK] Cleaned obj/Release" -ForegroundColor Green
}

# Summary
Write-Host ""
Write-Host "===========================================" -ForegroundColor Green
Write-Host "Package creation completed successfully!" -ForegroundColor Green
Write-Host "===========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Package Details:" -ForegroundColor Yellow
Write-Host "  Name:     $packageName.zip" -ForegroundColor White
Write-Host "  Location: $packagePath" -ForegroundColor White
Write-Host ("  Size:     {0:F2} MB" -f $zipSize) -ForegroundColor White
Write-Host ""
Write-Host "To distribute:" -ForegroundColor Yellow
Write-Host "  1. Share the .zip file: $packageName.zip" -ForegroundColor White
Write-Host "  2. Users extract and double-click Launch.bat to start the server" -ForegroundColor White
Write-Host ""

