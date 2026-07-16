# Script to create a redistributable package of AlyCE Log Analyzer
# This script builds the application in Release mode and packages it for distribution

param(
    [string]$PackageOutputPath = "./dist",
    [switch]$SkipBuild = $false,
    [switch]$SkipClean = $false
)

$ErrorActionPreference = "Stop"
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "AlyCE Log Analyzer - Release Package Builder" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

# Define paths
$projectPath = $scriptPath
$projectFile = Join-Path $projectPath "LogAnalyzer.Maui.csproj"
$binPath = Join-Path $projectPath "bin\Release"
$objPath = Join-Path $projectPath "obj\Release"
$publishPath = Join-Path $projectPath "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
# Handle both absolute and relative paths
if ([System.IO.Path]::IsPathRooted($PackageOutputPath)) {
    $packagePath = $PackageOutputPath
} else {
    $packagePath = Join-Path (Get-Location) $PackageOutputPath
}
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$packageName = "AlyCE-LogAnalyzer-v1.0_$timestamp"

Write-Host "Project Path: $projectPath" -ForegroundColor Yellow
Write-Host "Package Output: $packagePath" -ForegroundColor Yellow
Write-Host ""

# Clean previous builds if not skipped
if (-not $SkipClean) {
    Write-Host "Cleaning previous builds..." -ForegroundColor Cyan
    if (Test-Path $binPath) {
        Remove-Item -Path $binPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  ✓ Cleaned bin/Release" -ForegroundColor Green
    }
    if (Test-Path $objPath) {
        Remove-Item -Path $objPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  ✓ Cleaned obj/Release" -ForegroundColor Green
    }
}

# Build the project in Release mode if not skipped
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "Building project in Release mode..." -ForegroundColor Cyan
    
    $buildArgs = @(
        "build",
        $projectFile,
        "-c", "Release",
        "-f", "net10.0-windows10.0.19041.0",
        "--runtime", "win-x64",
        "--self-contained",
        "-p:PublishTrimmed=true",
        "-p:PublishReadyToRun=true"
    )
    
    & dotnet @buildArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "  ✓ Build completed successfully" -ForegroundColor Green
}

# Publish the application
Write-Host ""
Write-Host "Publishing application..." -ForegroundColor Cyan

$publishArgs = @(
    "publish",
    $projectFile,
    "-c", "Release",
    "-f", "net10.0-windows10.0.19041.0",
    "--runtime", "win-x64",
    "--self-contained",
    "-p:PublishTrimmed=false",
    "-p:PublishReadyToRun=true",
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-p:SelfContained=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
)

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ Publish completed successfully" -ForegroundColor Green

# Create output directory if it doesn't exist
if (-not (Test-Path $packagePath)) {
    New-Item -ItemType Directory -Path $packagePath | Out-Null
    Write-Host "  ✓ Created output directory: $packagePath" -ForegroundColor Green
}

# Create the package directory structure
Write-Host ""
Write-Host "Creating redistributable package..." -ForegroundColor Cyan

$packageDir = Join-Path $packagePath $packageName
if (-not (Test-Path $packageDir)) {
    New-Item -ItemType Directory -Path $packageDir | Out-Null
}

# Create Application subfolder
$appDir = Join-Path $packageDir "Application"
if (-not (Test-Path $appDir)) {
    New-Item -ItemType Directory -Path $appDir | Out-Null
}

# Copy published files to Application subfolder
Write-Host "  Copying application files..." -ForegroundColor Yellow
Copy-Item -Path "$publishPath\*" -Destination $appDir -Recurse -Force

# Read README content from README-HOW-TO.txt file
$readmeSourcePath = Join-Path $projectPath "README-HOW-TO.txt"
if (Test-Path $readmeSourcePath) {
    $readmeContent = Get-Content -Path $readmeSourcePath -Raw
} else {
    Write-Host "  Warning: README-HOW-TO.txt not found at $readmeSourcePath" -ForegroundColor Yellow
    $readmeContent = "See README-HOW-TO.txt for usage instructions."
}

$readmePath = Join-Path $packageDir "README.txt"
Set-Content -Path $readmePath -Value $readmeContent -Encoding UTF8
Write-Host "  ✓ Added README.txt" -ForegroundColor Green

# Create a shortcut to the exe for easy launching
Write-Host "  Creating application shortcut..." -ForegroundColor Yellow
$exePath = Join-Path $appDir "LogAnalyzer.Maui.exe"
$shortcutPath = Join-Path $packageDir "LogAnalyzer.Maui.lnk"

$WshShell = New-Object -ComObject WScript.Shell
$shortcut = $WshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $appDir
$shortcut.Description = "AlyCE Log Analyzer - Click to launch"
$shortcut.Save()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($WshShell) | Out-Null
Write-Host "  ✓ Created LogAnalyzer.Maui shortcut" -ForegroundColor Green

# Create ZIP archive
Write-Host ""
Write-Host "Creating ZIP archive..." -ForegroundColor Cyan

$zipPath = Join-Path $packagePath "$packageName.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
# Create ZIP with full path to package directory
[System.IO.Compression.ZipFile]::CreateFromDirectory($packageDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "  ✓ Created ZIP archive: $(Split-Path -Leaf $zipPath)" -ForegroundColor Green

# Get package size
$zipSize = (Get-Item $zipPath).Length / 1MB
Write-Host ("  ✓ Package size: {0:F2} MB" -f $zipSize) -ForegroundColor Green

# Create a manifest file
$manifestContent = @"
Package Name: $packageName
Creation Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Application: AlyCE Log Analyzer
Version: 1.0
Target Platform: Windows (x64)
Runtime: .NET 10.0 (Self-contained)
Size: {0:F2} MB

Contents:
- Fully self-contained application
- No additional runtime installation required
- Ready to run on Windows 10.0.19041 or later
- Shortcut for easy launching
- Comprehensive README with usage instructions
"@ -f $zipSize

$manifestPath = Join-Path $packagePath "$packageName.manifest"
Set-Content -Path $manifestPath -Value $manifestContent
Write-Host "  ✓ Created manifest file" -ForegroundColor Green

# Cleanup temporary directory after ZIP is created
Remove-Item -Path $packageDir -Recurse -Force

# Summary
Write-Host ""
Write-Host "===========================================" -ForegroundColor Green
Write-Host "Package creation completed successfully!" -ForegroundColor Green
Write-Host "===========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Package Details:" -ForegroundColor Yellow
Write-Host "  Name: $packageName.zip" -ForegroundColor White
Write-Host "  Location: $packagePath" -ForegroundColor White
Write-Host ("  Size: {0:F2} MB" -f $zipSize) -ForegroundColor White
Write-Host ""
Write-Host "To distribute:" -ForegroundColor Yellow
Write-Host "  1. Share the .zip file: $packageName.zip" -ForegroundColor White
Write-Host "  2. Users can extract and run Launch.bat or LogAnalyzer.Maui.exe" -ForegroundColor White
Write-Host ""
