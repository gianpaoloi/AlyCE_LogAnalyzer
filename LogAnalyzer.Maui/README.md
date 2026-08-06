# AlyCE Log Analyzer — .NET MAUI Blazor Hybrid

A native desktop rebuild of the AlyCE Log Analyzer. It hosts the **same Blazor components and
services** as the server app inside a MAUI `BlazorWebView` (WebView2 on Windows) — so it runs
**in-process** with no Kestrel server, no SignalR, no `localhost`, and with direct file-system access.

Build status: compiles clean (**0 errors, 0 warnings**) for `net10.0-windows10.0.19041.0`.

## Run

```powershell
cd LogAnalyzer.Maui
dotnet build -f net10.0-windows10.0.19041.0
dotnet run   -f net10.0-windows10.0.19041.0
```

Requires the **.NET MAUI** workload (`dotnet workload install maui-windows`) and the **WebView2
Runtime** (preinstalled on Windows 11).

## WebView2 Runtime (the "no compatible WebView2" failure)

The UI is Blazor inside a `BlazorWebView`, so the app is dead without the Edge **WebView2 Runtime** — a
separate OS component that ships with Windows 11 but is missing on plenty of Windows 10 images. Left
unhandled, MAUI throws *"Couldn't find a compatible WebView2 Runtime installation to host WebViews"* before
any window appears. Three layers now cover it:

| Layer | Behaviour |
|---|---|
| `installer/setup.iss` → `PrepareToInstall` | Probes the Evergreen registry key; if absent, downloads the Microsoft bootstrapper and runs it `/silent /install`. Runs during **silent installs too** (that's why it isn't a `[Run]` entry), so `winget install` provisions it as well. A download or install failure warns but does not abort. |
| `winget/manifests/…installer.yaml` | Declares `Microsoft.EdgeWebView2Runtime` under `Dependencies.PackageDependencies`, so winget can resolve it first. |
| `Platforms/Windows/WebView2Runtime.cs` | Last resort for the ZIP/xcopy deployment: the WinUI `App` constructor probes the same registry key and, when missing, shows a native message box offering the download page, then exits cleanly instead of crashing. |

The runtime installs **per-user without admin rights**, which matches this per-user setup.

## What it does

Identical feature set to the server app:

- **Overview / Dashboard / Explorer / Triage / Live watch** pages.
- Load logs from a **folder / UNC path**, or by dropping / picking `.log` files or a ZIP of them. The load
  panel **collapses** into a one-line summary via its *Load files* header, on every page that shows it;
  Live watch's *Watch settings* card collapses the same way.
- A **spinner with the current phase** (looking for files → parsing *n/m* files → sorting & computing stats)
  while a load is in flight, next to the Load button and in place of the page body.
- Explorer & Live: fixed columns incl. **Company**, per-column **combo filters** in the headers,
  **Add columns…** picker, resizable columns, a hidable **fully-expanded logger tree**,
  **Download** of the filtered rows (CSV or original `.log`), and **click a row** for the full-detail dialog.
- Explorer: a collapsible **log volume time series** over the filtered rows — stacked by level, with an
  automatic bucket size, a y axis scaled to the actual volume, and per-level totals in the legend. It is also
  a filter: **drag across the bars** to pick a time window, **click a legend entry** to toggle that level.
- Filters and panel states persist across navigation (`SessionState`, scoped per WebView).
- Dark navy/purple theme (Radzen `material-dark`, re-mapped in `app.css`).
- Live tailing of a local/UNC file.

## How this app was built (port from the server project)

### Shared through the `../LogAnalyzer.Core` project reference
Models, services and every page/component now live in **`LogAnalyzer.Core`** and are referenced by both hosts
— they are no longer copied per project, so a change lands in the server app and here at once:

- **`Models/`** — `LogEntry`, `LogFilter`, `LogColumns`, `Stats`, `TimeRange`.
- **`Services/`** — `LogParser`, `LogStore`, `LogWatcher`, `LogExport`, `MessageNormalizer`,
  `SessionState`, `ChartColors`.
- **`Components/Pages/`** — `Home` (Overview), `Dashboard`, `Explorer`, `Triage`, `Live`, `QuickStart`.
- **`Components/Shared/`** — `LoadPanel` (collapsible header), `LoadProgress` (spinner + load phase),
  `LogVolumeChart`, `LevelBadge`, `LogDetail`, `LoggerTree`.
- **`Components/Layout/`** — `MainLayout` (+ collapsible sidebar CSS), `NavMenu`.

This project keeps only the MAUI shell (`MauiProgram.cs`, `MainPage.xaml`, `Components/Routes.razor`,
`Components/_Imports.razor`, `Platforms/Windows/` incl. the WebView2 pre-flight check) and its own
**`wwwroot/`** — `app.css` (dark theme + component styles), `index.html`, `download.js`, `favicon.png`.

> `wwwroot/app.css` is **still a separate copy** from `../LogAnalyzer/wwwroot/app.css`: styles for shared
> components (e.g. the `.lv-*` volume-chart, `.load-busy-*` spinner and `.collapse-*` rules) have to be
> added to both files.

### Host shell — what changed vs the server project

| Concern | Server app | This app (MAUI Hybrid) |
|---------|-----------|------------------------|
| Host | `Program.cs` + `App.razor` with `@rendermode InteractiveServer` on `Routes`/`HeadOutlet` | `MauiProgram.cs` + `MainPage.xaml` (`BlazorWebView`) + `wwwroot/index.html`; **no render-mode directives** (hybrid is always interactive, in-process) |
| Router | `App.razor` document + `Routes.razor` | `Router`-based `Routes.razor` with `NotFoundPage`; template sample pages (Counter/Weather) removed |
| Theme / JS | `<RadzenTheme Theme="material-dark" />` component in `<head>` | static `<link>` to `_content/Radzen.Blazor/css/material-dark-base.css` + `<script>` for `Radzen.Blazor.js` and `download.js` in `index.html` |
| Config | `appsettings.json` (`LogAnalyzer:DefaultLogFolder`) | in-memory config in `MauiProgram.cs` |
| DI | `LogStore`/`LogWatcher` singletons, `SessionState` scoped, `AddRadzenComponents()`, SignalR message-size tuning | same service registrations in `MauiProgram.cs`; **no SignalR config** |
| Imports | web SDK implicit usings | `_Imports.razor` adds `Microsoft.Extensions.Configuration`, the `LogAnalyzer.Models` / `LogAnalyzer.Services` and Radzen namespaces |

`Models/` and `Services/` keep their original `LogAnalyzer.Models` / `LogAnalyzer.Services`
namespaces; only the Razor components live under `LogAnalyzer.Maui.Components.*`.

## Package as a redistributable

`WindowsPackageType` is `None`, so you can publish a self-contained unpackaged app:

```powershell
dotnet publish -f net10.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None
```

Output under `bin\Release\...\win10-x64\publish\` runs as a normal `.exe`. Wrap that folder with
Inno Setup for an installer, or set `WindowsPackageType=MSIX` (with `Platforms/Windows/Package.appxmanifest`)
for a Store/MSIX package.

### Automated Release Package Builder

Use the `create-release-package.ps1` script to build a complete redistributable package:

```powershell
.\create-release-package.ps1
```

#### Features

- **Automated build & publish** in Release mode with optimizations:
  - Trimmed runtime (smaller package size)
  - Ready-to-Run compilation (faster startup)
  - Debug symbols removed
- **Self-contained** — no .NET runtime installation required on target machines
- **Creates distributable ZIP** with timestamped naming
- **Includes README.txt** with installation instructions
- **Generates Launch.bat** for easy execution
- **Creates manifest file** with package metadata

#### Options

```powershell
# Skip build step (use existing build)
.\create-release-package.ps1 -SkipBuild

# Skip cleaning old builds
.\create-release-package.ps1 -SkipClean

# Custom output directory
.\create-release-package.ps1 -PackageOutputPath "C:\MyPackages"

# Combine options
.\create-release-package.ps1 -SkipBuild -PackageOutputPath "D:\releases"
```

#### Output

Outputs to `./dist/` by default:
- **AlyCE-LogAnalyzer-v1.0_YYYYMMDD_HHMMSS.zip** — Ready-to-distribute package
- **AlyCE-LogAnalyzer-v1.0_YYYYMMDD_HHMMSS.manifest** — Package metadata
- **README.txt** — Installation & usage instructions
- **Launch.bat** — Easy launch script for end users

The ZIP is fully self-contained and ready for distribution to Windows users.

## Notes / verify on first run

The project was verified by compilation only (headless build). When you launch the window, sanity-check
the two hybrid-specific behaviors:

1. The Radzen **dark theme** loads from the static CSS link in `index.html`.
2. The **ZIP-upload** file picker and the **CSV/.log download** trigger the native WebView2 dialogs.

To exercise the WebView2 pre-flight message on a machine that *has* the runtime, temporarily point
`WebView2Runtime.ClientKey` at a non-existent GUID — the probe is registry-only, so no uninstall is needed.

If either misbehaves in WebView2, the fallback is to switch those to native MAUI `FilePicker` /
`FileSaver` (CommunityToolkit.Maui). Everything else mirrors the server app exactly.
