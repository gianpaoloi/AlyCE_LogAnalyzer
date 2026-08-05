# AlyCE Log Analyzer

A Windows desktop application for parsing, browsing, and filtering structured log files. Built with .NET MAUI + Blazor.

## Features

- Load log files from a local folder or a ZIP archive — drag & drop supported
- Interactive filtering and search across all columns
- Log volume time series stacked by level — drag it to filter a time window
- Real-time log tailing / live monitoring
- Export filtered results to CSV or original log format
- Collapsible load panel and side navigation to maximise screen space
- Dark theme UI optimized for extended viewing sessions

## System Requirements

- Windows 10 version 17763 (1809) or later — x64
- No .NET runtime required (self-contained)

---

## Install via winget

```
winget install TeamSystem.AlyCELogAnalyzer
```

> **Note:** the package is available in the winget community repository once the manifest has been submitted.  
> See [`winget/manifests/`](winget/manifests/) for the manifest files.

## Manual Install

Download the latest `AlyCE-LogAnalyzer-Setup-{version}.exe` from the [Releases](../../releases) page and run it.

---

## Build from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- MAUI workload: `dotnet workload install maui-windows`

### Build & Run (development)

```powershell
cd LogAnalyzer.Maui
dotnet run -f net10.0-windows10.0.19041.0
```

### Create a release package (local)

```powershell
cd LogAnalyzer.Maui
.\create-release-package.ps1
# Output ZIP is placed in .\dist\
```

### Create the installer (requires [Inno Setup 6](https://jrsoftware.org/isinfo.php))

```powershell
# 1. Publish the app first
dotnet publish LogAnalyzer.Maui/LogAnalyzer.Maui.csproj `
    -c Release -f net10.0-windows10.0.19041.0 `
    --runtime win-x64 --self-contained

# 2. Compile the installer
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
    /DMyAppVersion=1.0.0 `
    LogAnalyzer.Maui/installer/setup.iss
# Output: LogAnalyzer.Maui/dist/AlyCE-LogAnalyzer-Setup-1.0.0.exe
```

---

## Publishing a New Release

1. Push a version tag — the CI workflow does the rest:
   ```
   git tag v1.0.0
   git push origin v1.0.0
   ```
2. GitHub Actions builds the app, compiles the installer, and creates a GitHub Release with:
   - The installer `.exe`
   - The installer SHA256 hash (needed for the winget manifest)

3. Update `winget/manifests/` with the `{{INSTALLER_URL}}` and `{{SHA256}}` from the Release notes.

4. Submit to winget:
   - Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)
   - Copy the three files from `winget/manifests/` to:  
     `manifests/t/TeamSystem/AlyCELogAnalyzer/1.0.0/`
   - Open a Pull Request — the winget bot validates automatically

---

## Projects

| Project | Description |
|---|---|
| `LogAnalyzer.Maui` | **Main** — Windows desktop app (MAUI + Blazor Hybrid) |
| `LogAnalyzer.Core` | Shared business logic and log parsing |
| `LogAnalyzer` | Alternative Blazor Server variant (self-hosted web app) |
