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

- **Overview / Explorer / Triage / Live watch** pages.
- Load logs from a **folder / UNC path**, or by dropping / picking `.log` files or a ZIP of them. The load
  panel **collapses** into a one-line summary via its *Load files* header, on every page that shows it;
  Live watch's *Watch settings* card collapses the same way.
- Both path boxes **remember the paths you used** (autocomplete, most recent first) — kept in the WebView's
  localStorage by `PathHistory`, so they survive restarts.
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
  `SessionState`, `PathHistory`, `ChartColors`.
- **`Components/Pages/`** — `Home` (Overview), `Explorer`, `Triage`, `Live`, `QuickStart`.
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

### Packaging scripts

There are two, for two different jobs.

| Script | Builds? | Archive name | Use it for |
|---|---|---|---|
| `create-portable-zip.ps1` | **No** — packages an existing publish output | `AlyCE-LogAnalyzer-{version}-win-x64.zip` | Release artifacts. This is what the CI workflow calls. |
| `create-release-package.ps1` | Yes — cleans, builds, publishes, zips | `AlyCE-LogAnalyzer-v1.0_{timestamp}.zip` | A quick one-shot package on your own machine. |

#### `create-portable-zip.ps1` — release packaging

Takes a publish directory and wraps it, deliberately building nothing. That is the point: the
release workflow publishes **once** and feeds the same output to both this script and Inno Setup, so
the installer and the ZIP cannot end up containing different binaries.

```powershell
# Publish first
dotnet publish LogAnalyzer.Maui.csproj -c Release -f net10.0-windows10.0.19041.0 `
    --runtime win-x64 --self-contained -p:PublishReadyToRun=true

# Then package
.\create-portable-zip.ps1 `
    -PublishDir "bin/Release/net10.0-windows10.0.19041.0/win-x64/publish" `
    -Version 1.2.3 `
    -OutputDir dist `
    -HowToPath ../README-HOW-TO.txt
```

| Parameter | |
|---|---|
| `-PublishDir` | **Required.** Rejected if it doesn't contain `LogAnalyzer.Maui.exe`, so a half-built directory can't yield a plausible-looking ZIP that won't start. |
| `-Version` | **Required.** Used in the archive name and the README header. |
| `-OutputDir` | Defaults to `./dist`. |
| `-HowToPath` | Optional usage instructions appended to the archive's `README.txt`. |

Produces a single top-level folder, so extracting it can't scatter several hundred runtime files
into the user's current directory:

```
AlyCE-LogAnalyzer-1.2.3-win-x64/
├── Application/       the publish output
├── Launch.bat         starts Application\LogAnalyzer.Maui.exe via a path relative to itself
└── README.txt         version header, WebView2 note, then README-HOW-TO.txt
```

The launcher is a `.bat` rather than a `.lnk` on purpose: a shortcut stores the absolute path it was
created with, which on a build agent points at a directory the end user does not have.

#### `create-release-package.ps1` — local one-shot

```powershell
.\create-release-package.ps1                                  # clean, build, publish, zip
.\create-release-package.ps1 -SkipBuild                       # reuse the existing build
.\create-release-package.ps1 -SkipClean                       # keep previous build output
.\create-release-package.ps1 -PackageOutputPath "D:\releases" # custom output directory
```

Outputs to `./dist/`: the `.zip`, plus a `.manifest` with package metadata. The archive is
self-contained (Ready-to-Run, debug symbols stripped, **not** trimmed) and contains `Application/`,
`README.txt` and a `.lnk` shortcut.

> The name is `<ApplicationDisplayVersion>` read from the csproj plus a timestamp — so it says
> `v1.0.0` for any local build, with no tag involved — and the `.lnk` records an absolute path. Treat
> this archive as a local convenience; use `create-portable-zip.ps1` for anything you hand to
> someone else.

## Notes / verify on first run

The project was verified by compilation only (headless build). When you launch the window, sanity-check
the two hybrid-specific behaviors:

1. The Radzen **dark theme** loads from the static CSS link in `index.html`.
2. The **ZIP-upload** file picker and the **CSV/.log download** trigger the native WebView2 dialogs.

To exercise the WebView2 pre-flight message on a machine that *has* the runtime, temporarily point
`WebView2Runtime.ClientKey` at a non-existent GUID — the probe is registry-only, so no uninstall is needed.

If either misbehaves in WebView2, the fallback is to switch those to native MAUI `FilePicker` /
`FileSaver` (CommunityToolkit.Maui). Everything else mirrors the server app exactly.
