<#
.SYNOPSIS
    Packages an already-published AlyCE Log Analyzer build into a redistributable ZIP.

.DESCRIPTION
    Takes the output of `dotnet publish` and wraps it into a portable archive. It deliberately does
    NOT build or publish anything: the release workflow publishes once and feeds the same output to
    both this script and the Inno Setup installer, so the two downloads are guaranteed to contain
    identical binaries.

    For a one-shot local package that also does the build, use create-release-package.ps1 instead.

.PARAMETER PublishDir
    The self-contained publish output to package.

.PARAMETER Version
    Version string used in the archive name and the README header, e.g. 1.2.3.

.PARAMETER OutputDir
    Where the .zip is written. Created if missing.

.PARAMETER HowToPath
    Optional usage instructions appended to the archive's README.txt.

.EXAMPLE
    ./create-portable-zip.ps1 -PublishDir bin/Release/net10.0-windows10.0.19041.0/win-x64/publish `
                              -Version 1.2.3 -OutputDir dist
#>
param(
    [Parameter(Mandatory = $true)] [string]$PublishDir,
    [Parameter(Mandatory = $true)] [string]$Version,
    [string]$OutputDir = "./dist",
    [string]$HowToPath
)

$ErrorActionPreference = "Stop"

$exeName = "LogAnalyzer.Maui.exe"
$packageName = "AlyCE-LogAnalyzer-$Version-win-x64"

if (-not (Test-Path -LiteralPath $PublishDir)) {
    throw "Publish directory not found: $PublishDir. Run dotnet publish first."
}

# Guard against packaging an empty or half-built directory, which would produce a plausible-looking
# ZIP that cannot start.
$exeSource = Join-Path $PublishDir $exeName
if (-not (Test-Path -LiteralPath $exeSource)) {
    throw "$exeName not found in $PublishDir - that does not look like a published build."
}

[System.IO.Directory]::CreateDirectory($OutputDir) | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path

# Staged under the output directory so a failed run leaves nothing behind in the source tree.
$stagingRoot = Join-Path $OutputDir ".staging-$([guid]::NewGuid().ToString('N'))"
$packageDir = Join-Path $stagingRoot $packageName
$appDir = Join-Path $packageDir "Application"

# Paths go through .NET rather than the Remove-Item / wildcard cmdlets throughout: -OutputDir can
# legitimately sit under a directory whose name contains a '~' (an 8.3 short name such as
# C:\Users\G10C6~1.IAN\AppData\Local\Temp), and `Remove-Item -Recurse` fails outright on those.
try {
    Write-Host "Staging $packageName ..." -ForegroundColor Cyan
    [System.IO.Directory]::CreateDirectory($appDir) | Out-Null

    # Enumerate-then-copy instead of a "$PublishDir\*" wildcard, which would also break on a path
    # containing [ or ].
    foreach ($item in Get-ChildItem -LiteralPath $PublishDir -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $appDir -Recurse -Force
    }

    # A .bat with a path relative to itself, not a .lnk: a shortcut records the absolute path it was
    # created with, which on a build agent points at a directory the user does not have.
    $launcher = @"
@echo off
rem Starts AlyCE Log Analyzer from wherever this folder has been extracted.
start "" "%~dp0Application\$exeName"
"@
    [System.IO.File]::WriteAllText((Join-Path $packageDir "Launch.bat"), $launcher, [System.Text.Encoding]::ASCII)

    $readme = @"
AlyCE Log Analyzer $Version - portable package (Windows x64)

To run:
  Double-click Launch.bat, or run Application\$exeName directly.

No .NET runtime is required; this build is self-contained.

Microsoft Edge WebView2 Runtime:
  The app's interface runs in WebView2, which is a separate Windows component. It ships with
  Windows 11 and with most up-to-date Windows 10 installations. This portable package does NOT
  install it - unlike the Setup .exe, which adds it automatically when missing. If the app reports
  it is unavailable on startup, install it from:
  https://developer.microsoft.com/microsoft-edge/webview2/

--------------------------------------------------------------------------------

"@

    if ($HowToPath -and (Test-Path -LiteralPath $HowToPath)) {
        $readme += (Get-Content -LiteralPath $HowToPath -Raw)
    }
    else {
        if ($HowToPath) { Write-Warning "Usage instructions not found at $HowToPath" }
        $readme += "See the project README for usage instructions."
    }

    [System.IO.File]::WriteAllText((Join-Path $packageDir "README.txt"), $readme, (New-Object System.Text.UTF8Encoding($false)))

    $zipPath = Join-Path $OutputDir "$packageName.zip"
    if ([System.IO.File]::Exists($zipPath)) { [System.IO.File]::Delete($zipPath) }

    if (-not ('System.IO.Compression.ZipFile' -as [type])) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
    }

    # includeBaseDirectory: the archive holds one top-level folder, so extracting it cannot scatter
    # several hundred runtime files into whatever directory the user happened to be in.
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $packageDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $true)

    $sizeMb = (Get-Item -LiteralPath $zipPath).Length / 1MB
    Write-Host ("Created {0} ({1:F1} MB)" -f (Split-Path -Leaf $zipPath), $sizeMb) -ForegroundColor Green

    # Hand the path back to the release workflow when running under GitHub Actions.
    if ($env:GITHUB_OUTPUT) {
        "ZIP=$zipPath"              | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "ZIP_NAME=$packageName.zip" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }

    $zipPath
}
finally {
    if ([System.IO.Directory]::Exists($stagingRoot)) {
        try { [System.IO.Directory]::Delete($stagingRoot, $true) }
        catch { Write-Warning "Could not remove staging directory $stagingRoot : $($_.Exception.Message)" }
    }
}
