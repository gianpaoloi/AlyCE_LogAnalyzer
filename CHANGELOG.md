# Changelog

All notable changes to **AlyCE Log Analyzer**, newest first. Entries reference the commit they come from.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The repository has no version
tags: `1.0.0` is the version published to winget (`ApplicationDisplayVersion` in
`LogAnalyzer.Maui.csproj` and `PackageVersion` in the winget manifests), and everything committed after that
release is collected under *Unreleased*.

## [Unreleased]

### Added

- **Auto-scroll toggle on Live watch** — untick *auto-scroll* to freeze the grid on the lines currently shown
  while the tail keeps parsing and buffering behind it, so a row can be read or clicked without moving. Column
  filters, the logger tree and download still work on the frozen set; the status bar gains an *auto-scroll
  off* badge and a *Show N new lines* button to catch up. New `SessionState.LiveAutoScroll`.
  *(working tree, not yet committed)*
- **File picker on Live watch** — a *Browse…* button opens `FileBrowserDialog`, a filesystem browser
  (drives → folders → files, with size and modified date, folders by name and files newest-first), so the file
  to tail no longer has to be typed. It browses the machine that reads the logs — the server for the web host,
  the desktop for MAUI — which is the same machine `LogWatcher` tails from, so local and UNC paths both work.
  Lists `.log` only until *all files* is ticked. (`1becf5e`)
- **Favorites in the file browser** — *Add to favorites* pins the selected file, or the current folder when
  nothing is selected. Pinned paths show as chips (last segment, full path in the tooltip); click to jump
  there, `×` to unpin. Persisted by `PathHistory` under `alyce.pathHistory.favorites`, capped at 30, so they
  survive a restart. `PathHistory` gained `RemoveAsync` and an optional per-key cap on `AddAsync`.
  (`1becf5e`)
- **Path history** on the two path boxes (log folder, Live watch file): autocompletes that suggest paths
  already used on this machine, most recent first, de-duplicated case-insensitively, capped at 12, and only
  recorded once a path actually worked. Stored in localStorage by the new `Services/PathHistory.cs` so they
  survive an app restart, unlike `SessionState`. (`ba7886b`)
- **Interactive log volume chart** on Explorer — drag a time window on the chart to filter, click a level in
  the legend to toggle it; new `Models/TimeRange.cs` carries the brushed range through `SessionState`.
  (`840bdad`)
- **Log volume chart** (`LogVolumeChart.razor`) — Grafana-style stacked-by-level time series of the currently
  filtered rows, above the Explorer grid. (`58c123e`)
- **Collapsible load panel** with a one-line summary while folded (entries, files, source path, or load
  progress), shared across every page through `SessionState.LoadPanelCollapsed`, plus a `LoadProgress`
  spinner component. (`58c123e`)
- **Collapsible *Watch settings* card** on Live watch, with its own `SessionState.LiveSettingsCollapsed` flag
  and a summary showing the watched file and the active text filter; raw detail also folds on click.
  (`25a57d3`)
- **WebView2 runtime pre-flight** — `Platforms/Windows/WebView2Runtime.cs` probes the registry on startup and
  tells the user where to get the runtime instead of failing with a blank window; the Inno Setup installer
  downloads and installs it silently when missing. (`0419db2`)

### Changed

- Live watch no longer freezes on a wrong or offline path. Every filesystem probe in the file browser runs off
  the UI thread under a 3 s timeout — `File.Exists` / `Directory.Exists` / `DriveInfo.IsReady` block for ~30 s
  on a dead UNC path, and the probing used to happen during dialog initialisation, so the app appeared hung
  before the dialog even painted. Existence checks and listing now share a single probe. Starting a watch was
  moved off the UI thread for the same reason; the button reads *Opening…* while the path resolves.
  (`1becf5e`)
- A wrong, unreachable or empty path in the file browser falls back to the system drive (`C:\`) with the
  reason shown in a warning, instead of an empty drive list. (`1becf5e`)
- Live watch records a path in the history once the watcher actually opened the file, rather than on a second
  blocking `File.Exists`. (`1becf5e`)
- Repository homepage / support links point at the public GitHub repo. (`64f79a4`)

## [1.0.0] — 2026-07-23

### Added

- **winget packaging and release automation** — an Inno Setup installer (`LogAnalyzer.Maui/installer/setup.iss`),
  winget manifests under `winget/manifests/` for `TeamSystem.AlyCELogAnalyzer`, a GitHub Actions release
  workflow (`.github/workflows/release.yml`) that builds and publishes the installer, and `winget-readme.md`
  documenting the submission. (`e407084`)

## 2026-07-16 — initial development

### Added

- **First working application** — a Blazor UI on Radzen components, in two hosts sharing one codebase: a
  .NET MAUI desktop app (WebView2) and an ASP.NET Core Blazor Server app. (`966fa67`)
  - **Pages**: Overview (totals and breakdown charts), Dashboard (volume per hour, errors/warnings per hour),
    Explorer (searchable paginated grid with per-column combo filters, logger tree, column picker, row
    detail), Triage (clusters similar ERROR/WARN messages into issue groups), Live watch (tails one file on a
    local or UNC path in real time).
  - **Services**: `LogParser` (JSON-lines log entries), `LogStore` (loads a folder or a ZIP, reports
    progress), `LogWatcher` (polling tail that works across network shares and while the writer holds the
    file), `MessageNormalizer` (masks guids/numbers/durations for clustering), `LogExport` (CSV with BOM for
    Excel, or original JSON lines), `SessionState` (filters that persist across navigation).
  - Drag & drop or click-to-pick loading of `.log` and `.zip` files, dark theme, `create-release-package.ps1`
    to produce a self-contained ZIP.
- **Quick start** page, linked from the top bar; the duplicated Blazor error banner was dropped from the
  layout and `index.html`. (`74c45cf`)
- Repository README. (`91b1c1f`, `d5f805f`)

### Changed

- **Shared `LogAnalyzer.Core` project** — pages, shared components, models and services moved out of the two
  host projects into one Razor class library referenced by both, so the MAUI app and the server app can no
  longer drift apart. The server host registers the extra assembly for routing. (`f475cf9`)

### Fixed

- Server host would not run the shared components: added the additional routing assembly, dropped the HTTPS
  redirect and HSTS that broke local use, and registered the native drop-zone JS (`registerNativeDropInput`)
  so click-to-pick and drag & drop work in the browser as well as in the WebView. (`0c7f339`)
