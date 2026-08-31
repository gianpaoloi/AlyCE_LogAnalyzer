# AlyCE Log Analyzer

A Windows desktop application for parsing, browsing, and filtering structured log files. Built with .NET MAUI + Blazor.

## Features

- Load log files from a local folder or a ZIP archive — drag & drop supported
- Path boxes remember previously used folders and files
- Interactive filtering and search across all columns
- Log volume time series stacked by level — drag it to filter a time window
- Real-time log tailing / live monitoring — pick the file to watch with a built-in browser (with favorites),
  and switch auto-scroll off to read a line while the tail keeps running
- Export filtered results to CSV or original log format
- Collapsible load panel and side navigation to maximise screen space
- Dark theme UI optimized for extended viewing sessions

## System Requirements

- Windows 10 version 17763 (1809) or later — x64
- No .NET runtime required (self-contained)
- **Microsoft Edge WebView2 Runtime** — hosts the app's UI. Bundled with Windows 11; the installer
  downloads and installs it silently when missing (no admin rights needed). If you deploy the ZIP package
  instead, install it from
  [Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/) — the app says so on startup if it
  can't find it.

---

## Install via winget (WIP)

```
winget install TeamSystem.AlyCELogAnalyzer
```

> **Note:** the package is available in the winget community repository once the manifest has been submitted.  
> See [`winget/manifests/`](winget/manifests/) for the manifest files.

## Manual Install

Every release ships two downloads on the [Releases](../../releases) page. Both contain the same
self-contained build — no .NET runtime is required either way.

| Download | Use it when |
|---|---|
| `AlyCE-LogAnalyzer-Setup-{version}.exe` | **Recommended.** Installs per-user (no admin rights), adds Start-menu entries, and installs the WebView2 runtime if it is missing. |
| `AlyCE-LogAnalyzer-{version}-win-x64.zip` | Portable / redistributable. Extract anywhere and run `Launch.bat`. Nothing is installed and nothing is written to the registry — handy for a locked-down machine, a network share or a USB stick. Requires the WebView2 runtime to be present already (see [System Requirements](#system-requirements)). |

The ZIP extracts to a single folder:

```
AlyCE-LogAnalyzer-{version}-win-x64/
├── Application/       the app and its runtime
├── Launch.bat         starts it from wherever you extracted it
└── README.txt         usage instructions
```

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

### Run the tests

```powershell
dotnet test LogAnalyzer.Tests/LogAnalyzer.Tests.csproj
```

Covers the parser, the UTF-8 line reader, the store, the file tailer, the exporter and the filters.
The release workflow runs them too, and will not publish if they fail.

### Create a release package (local)

One-shot: cleans, builds, publishes and zips.

```powershell
cd LogAnalyzer.Maui
.\create-release-package.ps1
# Output ZIP is placed in .\dist\
```

### Reproduce the release artifacts locally

The same two steps CI runs, both fed by one publish — which is what guarantees the installer and the
ZIP contain identical binaries.

```powershell
# 1. Publish once
dotnet publish LogAnalyzer.Maui/LogAnalyzer.Maui.csproj `
    -c Release -f net10.0-windows10.0.19041.0 `
    --runtime win-x64 --self-contained `
    -p:PublishTrimmed=false -p:PublishReadyToRun=true `
    -p:DebugType=none -p:DebugSymbols=false

# 2a. Portable ZIP
.\LogAnalyzer.Maui\create-portable-zip.ps1 `
    -PublishDir "LogAnalyzer.Maui/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish" `
    -Version 1.0.0 -OutputDir "LogAnalyzer.Maui/dist" -HowToPath "README-HOW-TO.txt"
# Output: LogAnalyzer.Maui/dist/AlyCE-LogAnalyzer-1.0.0-win-x64.zip

# 2b. Installer (requires Inno Setup 6 — https://jrsoftware.org/isinfo.php)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
    /DMyAppVersion=1.0.0 `
    LogAnalyzer.Maui/installer/setup.iss
# Output: LogAnalyzer.Maui/dist/AlyCE-LogAnalyzer-Setup-1.0.0.exe
```

> `create-portable-zip.ps1` deliberately does not build anything — it packages an existing publish
> output, so it can be pointed at the same directory the installer reads.

---

## Publishing a New Release

1. Merge the work into `main`, then tag the merge commit and push the tag — the CI workflow
   ([`.github/workflows/release.yml`](.github/workflows/release.yml)) does the rest:
   ```
   git checkout main && git pull
   git tag v1.0.0
   git push origin v1.0.0
   ```
   > The workflow **refuses to release a tag that is not an ancestor of `main`**. A tag trigger fires
   > wherever the tag was placed, so without that check a version tag pushed from a feature branch
   > would publish a release built from unmerged code.

2. GitHub Actions runs the unit tests, publishes the app **once**, and packages that single output
   two ways — so the installer and the ZIP can never contain different binaries. It then creates a
   GitHub Release with:
   - `AlyCE-LogAnalyzer-Setup-{version}.exe` — the installer
   - `AlyCE-LogAnalyzer-{version}-win-x64.zip` — the portable/redistributable package
   - SHA256 for both (the installer's is the one the winget manifest needs)

   The tests are a gate: if they fail, nothing is published. Both artifacts are also uploaded as
   workflow artifacts (14-day retention), so they survive a failure in the release step itself.

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
| `LogAnalyzer.Tests` | Unit tests for `LogAnalyzer.Core` |
