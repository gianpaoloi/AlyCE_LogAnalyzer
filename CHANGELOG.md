# Changelog

All notable changes to **AlyCE Log Analyzer**, newest first. Entries reference the commit they come from.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The repository has no version
tags: `1.0.0` is the version published to winget (`ApplicationDisplayVersion` in
`LogAnalyzer.Maui.csproj` and `PackageVersion` in the winget manifests), and everything committed after that
release is collected under *Unreleased*.

## [Unreleased]

### Added

- **Automatic update check against GitHub Releases.** The app asks
  `api.github.com/repos/gianpaoloi/AlyCE_LogAnalyzer/releases/latest` once per run, compares the tag with the
  version the running assembly reports, and — only when the tag is newer — shows an *Update to v1.2.3* line
  above the version footer in the sidebar. Clicking it opens a dialog with the release notes, the download
  size and a **Download and install** button: the release's setup package is downloaded to `%TEMP%`, verified
  against the SHA256 published with the release, then run with `/SILENT` while the app closes so it can be
  replaced. `installer/setup.iss` gained a `/UPDATED=1` switch that restarts the app after a silent install —
  the existing launch step is `skipifsilent`, so without it the app would close to update and never come back.

  Versions are compared by SemVer precedence rather than as strings: `1.10.0` is newer than `1.9.0`, `1.3.0`
  is newer than `1.3.0-rc1`, and the SourceLink commit suffix is ignored so a local build of `1.2.3` does not
  see the released `1.2.3` as an update.

  Only a copy installed by the setup package offers to install. A portable or development build would install
  a *second* app into `%LOCALAPPDATA%` and carry on running the old one, so it is pointed at the download page
  instead — as is the self-hosted web build, which is a published folder someone replaces. A check that fails
  (offline, proxy, rate limit) shows nothing at all: this runs in the background of a log viewer and must not
  produce errors to dismiss. It is also the app's only outbound request, so it can be switched off with
  `LogAnalyzer:Updates:Enabled=false`. New `Services/Updates/` in Core, `Services/WindowsUpdateInstaller.cs`
  in the MAUI host, and 124 tests. *(working tree, not yet committed)*

- **The running version is shown at the bottom of the navigation sidebar**, always on screen. It reads
  `AssemblyInformationalVersion` at runtime — which the SDK stamps with the version and, via SourceLink, the
  exact commit — so it reports what is actually running and cannot go stale. Clicking it copies the full
  `1.2.3+<commit>` string, which is what a bug report needs. New `Services/AppVersion.cs`.
  *(working tree, not yet committed)*
- **A portable (redistributable) ZIP is now built and published alongside the installer** on every version
  tag. `create-portable-zip.ps1` wraps the publish output as
  `AlyCE-LogAnalyzer-{version}-win-x64.zip`, containing one top-level folder with `Application/`, a
  `Launch.bat` that resolves its own location, and a `README.txt` carrying the version, a WebView2 note and
  the usage instructions. The release workflow publishes **once** and feeds that same output to both the ZIP
  and Inno Setup, so the two downloads cannot contain different binaries. Both are attached to the release
  with their SHA256, and uploaded as workflow artifacts so they survive a failure in the release step.
  *(working tree, not yet committed)*
- **The release workflow refuses a tag that is not an ancestor of `main`.** A tag trigger fires wherever the
  tag was placed, so a version tag pushed from a feature branch would otherwise publish a release built from
  unmerged code. *(working tree, not yet committed)*
- **Unit test suite** (`LogAnalyzer.Tests`, 145 tests) covering the parser, the line reader, the store, the
  tailer, the exporter and the filters — including regression tests for every fix listed below. Runs in CI
  before a release is published. The message-signature scan is held against the regex pipeline it replaced
  over the real sample logs. *(working tree, not yet committed)*
- **Cancel button on the load panel.** A folder load on a slow or offline share could not be escaped: the
  store supported cancellation but nothing ever passed a token, so the *Load cancelled.* path was
  unreachable. *(working tree, not yet committed)*
- **Auto-scroll toggle on Live watch** — untick *auto-scroll* to freeze the grid on the lines currently shown
  while the tail keeps parsing and buffering behind it, so a row can be read or clicked without moving. Column
  filters, the logger tree and download still work on the frozen set; the status bar gains an *auto-scroll
  off* badge and a *Show N new lines* button to catch up. New `SessionState.LiveAutoScroll`. (`efe0a4d`)
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

### Fixed

- **Setting a filter on Live watch, or clicking away to Triage or Explorer, froze the whole app.** One lock was
  shared between the tail's ingest handler and `RebuildSnapshot`, which every header filter calls — on the
  renderer. In Blazor Server the renderer is the single thread that serializes every click, every redraw and the
  nav menu for the circuit, so blocking it there stopped everything until the batch in flight finished. And the
  batches are large: `LogWatcher` hands over up to 4 MB × 16 chunks per poll, so one call could loop a quarter
  of a million entries under that lock — of which, with a 1 000-line buffer, all but the last thousand were
  `AddFirst` immediately undone by `RemoveLast`. The buffer is now a fixed ring, the lock covers nothing but
  array writes or a bounded copy, filtering runs on that copy outside the lock, and each batch is cut to the
  newest 1 000 matches *before* anything is pushed. The distinct-value tallies behind the header combos left the
  lock entirely — the tail is their only writer, and the renderer reads published immutable copies.
  *(working tree, not yet committed)*
- **The Live watch text filter appeared to do nothing.** The box had no `@bind-Value:after`, so editing it
  triggered no redraw and left the lines buffered under the previous filter on screen — indistinguishable from a
  hang. It is now applied to the buffered lines as well as to the tail, so narrowing it takes effect
  immediately. *(working tree, not yet committed)*
- **The Live watch logger tree could throw or spin.** It enumerated the logger tally while the poll thread was
  still counting into it, which a `Dictionary` does not allow; it now gets a snapshot published by the tail,
  republished only when a logger is new to the session. *(working tree, not yet committed)*
- **Leaving Live watch always threw an `ObjectDisposedException` internally.** `Dispose` cancelled the refresh
  loop's token source and disposed it in the next statement, racing the loop's own
  `PeriodicTimer.WaitForNextTickAsync`. The loop now disposes it once it has unwound. Refreshing is also
  skipped entirely while auto-scroll is off, where handing the grid a new `Data` reference every 400 ms made it
  re-filter, re-sort and re-page for no visible change. *(working tree, not yet committed)*
- **Quick Start reported a version and build date that were typed by hand and weeks out of date.** The page
  renders `README-HOW-TO.txt`, which stated `Version: 1.0 / Build Date: 2026-07-16` regardless of what was
  actually running — so anyone quoting it in a bug report was quoting a fiction. That block is gone; the real
  version comes from the assembly and is shown in the sidebar. *(working tree, not yet committed)*
- **A prerelease tag published as a full release.** The workflow trigger `v*.*.*` is looser than it looks —
  the trailing `*` matches any suffix, so `v1.3.0-rc1` starts a release — while `prerelease:` was hardcoded
  to `false`. A version containing `-` is now published as a GitHub prerelease.
  *(working tree, not yet committed)*
- **Dropping a .log file could crash the Explorer with "collection was modified".** Appending mutated and
  re-sorted the very list a query was walking, from a thread-pool thread on the MAUI drop path. Loads now
  publish an immutable `LogDataset` and swap the reference, so a reader that has taken it can never see it
  change. Appending also merges the two already-sorted sides instead of re-sorting everything loaded so far,
  which made dropping N files cost N sorts of a growing list. *(working tree, not yet committed)*
- **Live watch could mix up two files.** Restarting a watch cancelled the previous poll loop but did not wait
  for it, then reset the shared read position and pending-line state underneath it — likely, because one poll
  on a slow share can sit in a blocking read for seconds. `StartAsync` now awaits `StopAsync`. The loop also
  swallowed only `OperationCanceledException`, leaving an unobserved `ObjectDisposedException` on the way out.
  *(working tree, not yet committed)*
- **A rotated log went quiet for good.** Rotation was only detected when the file *shrank*, so the usual
  pattern — rename `all.log` away, create a fresh one — left a new file growing past the old read position and
  nothing was ever read again. The tailer now fingerprints the first 256 bytes of the file it is following.
  Creation time alone is not enough: NTFS file tunneling gives a file recreated within ~15 s of its
  predecessor that predecessor's creation timestamp, which is exactly what rotation does.
  *(working tree, not yet committed)*
- **Catching up on a large file took minutes of waiting.** The 4 MB decode cap ended the poll rather than the
  chunk, so tailing a 1 GB file from the start took one 750 ms tick per 4 MB. Chunks now follow each other
  inside one poll (bounded, so the UI still breathes), and the file is opened once per poll instead of once
  per chunk — a round trip each time on a share. *(working tree, not yet committed)*
- **Lines sharing a timestamp were reordered on every load.** `List.Sort` is unstable, and the sample logs are
  full of duplicate timestamps — two lines logged in the same tick came out in an arbitrary order that changed
  from load to load. Files are now merged by time with ties broken on file order, so the same input always
  gives the same result. *(working tree, not yet committed)*
- **Timestamps depended on the machine's regional settings.** The fallback was a bare `DateTime.TryParse`,
  which reads the current culture; the same file parsed differently on an it-IT machine than on an en-US one,
  or silently became `DateTime.MinValue`. Now always invariant, and ISO-8601 with an offset is accepted.
  *(working tree, not yet committed)*
- **Live watch could not filter for FATAL.** The level dropdown was a hardcoded ERROR/WARN/INFO/DEBUG array,
  while the rest of the app has always treated FATAL and WARNING as real levels — a FATAL line was displayed
  but unfilterable. The list now grows with the levels actually seen in the tail.
  *(working tree, not yet committed)*
- **A failing store event could take the whole app down** instead of one component. The five `Store.Changed`
  handlers were `async void`, so an exception from dispatching to a renderer that had already gone away (a
  circuit closing as a load finishes) had no synchronization context to surface on. They now go through
  `ObservingComponentBase`. *(working tree, not yet committed)*
- **Dropping more than 100 files blanked the page.** `GetMultipleFiles` throws past its cap rather than
  truncating, and nothing caught it. *(working tree, not yet committed)*
- **Live watch started on a file that never existed.** The default path appended a hardcoded
  `all_2026-06-30.log` to the configured folder, so the first *Start watching* always failed.
  *(working tree, not yet committed)*
- **The web host showed every visitor the same logs.** `LogStore` and `LogWatcher` were singletons, so one
  dataset and one tail were shared by everyone on the server: whoever loaded a folder showed their logs to all
  other users, *include DEBUG* was global, and either user could stop the other's watch. Both are scoped to a
  circuit now. The desktop app keeps singletons — one user, one window. (This host still has no
  authentication, and both the file browser and the tailer will read any path the machine can reach; it should
  stay on localhost or go behind auth.) *(working tree, not yet committed)*
- **A large export could exhaust memory.** CSV was built as a `StringBuilder`, then a string, then a byte
  array — several copies of a set that can be hundreds of megabytes, all live at once, and the browser then
  made one more. Both formats now stream into a self-deleting temp file, and the JSON writer is created once
  instead of once per row. A leading `=`, `+` or `@` in a message is also defused, since log text ends up in a
  spreadsheet. *(working tree, not yet committed)*
- **Removed two pieces of dead code that were traps**: `LogFilter.Clone()` copied the levels but silently
  dropped the environment and company filters, and `LogStore.QueryPage` buffered the whole result twice.
  *(working tree, not yet committed)*
- **Live watch froze after watching for a long time.** Four unbounded growths, all in the tail path:
  the page queued a render per poll (and per status change), so the render queue outgrew the renderer — it
  now refreshes at most every 400 ms, only when something changed, and awaits each render;
  `LogWatcher` decoded the whole remainder of the file in one go when catching up, which allocated a
  large-object-heap string — capped at 4 MB per tick, with a `Decoder` kept across polls (which also fixes a
  multi-byte character split across two chunks decoding to garbage);
  `LogParser`'s intern pool retained every distinct `username` / `cid` / `company` for the life of the app,
  since the watcher keeps one parser — capped at 20 000 entries;
  the Environment / Company header combos and the logger tree collected new values for ever, making each
  refresh slower — capped at 500 values and 2 000 loggers, with known values still counting.
  *(working tree, not yet committed)*

### Removed

- **The Dashboard page.** Its two charts moved to Overview, which already carried the other half of the same
  information — the *By level* pie and *Top loggers* breakdowns were on both pages, so the two were mostly a
  split view of one dataset summary. `/dashboard` no longer resolves. *(working tree, not yet committed)*

### Changed

- **All projects now target .NET 10.** `LogAnalyzer.Core`, `LogAnalyzer` and `LogAnalyzer.Tests` moved from
  `net8.0` to `net10.0`; `LogAnalyzer.Maui` was already there, so the solution is on one framework for the
  first time. .NET 8 support ends in November 2026. Two knock-on cleanups: the pinned
  `Microsoft.Extensions.Configuration.Abstractions` 8.0.0 reference in `LogAnalyzer.Core` turned out to be
  redundant — the Razor SDK already supplies it through the shared framework — and was removed rather than
  bumped; and `LogAnalyzer/create-release-package.ps1`, which had `net8.0` hardcoded in four places, now
  reads the target framework and version out of the csproj so it cannot fall behind again.
- **The load panel folds itself away once a load succeeds**, on every page that shows it, so the results get
  the screen straight after loading. The folded header still reports the entries, files and source path. It
  unfolds again if a load fails or the dataset is cleared, since the error alert and the load controls are both
  inside the folded part. Driven by the store, so a file dropped onto the desktop window — which bypasses the
  panel — collapses it too. *(working tree, not yet committed)*
- **Overview now carries the whole dataset summary**: the totals and time span it already had, plus the log
  volume per time bucket stacked by level and the errors & warnings chart from the Dashboard, then the level /
  environment / logger breakdowns. Both timeline charts label their own bucket size, which adapts to the span.
  The bucket grouping moved out of the page into `TimelineView.Downsample` so it could be tested.
  *(working tree, not yet committed)*
- **The volume timeline no longer undercounts.** `TimeBucket` gained an *other* series, so levels outside
  DEBUG/INFO/WARN/ERROR — TRACE, a custom level, or a line with no `level` field at all, which the parser
  stores as `UNKNOWN` — are counted instead of silently dropped. The bars now add up to the entry total, and
  the series only appears when such entries exist. *(working tree, not yet committed)*
- **Loads are about twice as fast** (measured: 1 454 ms → 709 ms for 480 000 entries across 12 files,
  159 MB). Files are parsed on all cores with a parser each, lines are read as UTF-8 bytes rather than a
  string each, and each line goes through one `Utf8JsonReader` pass instead of a `JsonDocument` plus nine
  property lookups. Statistics and the filter indexes are computed side by side, and files that cover
  successive periods are concatenated instead of heap-merged. *(working tree, not yet committed)*
- **Progress no longer makes every page redo its work.** A load raised one event per file, and each one made
  the Explorer re-filter the whole previous dataset and triage re-cluster it — for a 60-file load, 60 times
  over, when only a counter had changed. `DatasetChanged` and a throttled `ProgressChanged` are now separate.
  *(working tree, not yet committed)*
- **Triage re-clusters about 6× faster** (404 ms → 57 ms on 480 000 entries) because a message's signature is
  computed once and cached on the entry, rather than seven chained `Regex.Replace` calls re-run on every
  checkbox toggle. The normaliser itself is now a single scan instead of seven passes.
  *(working tree, not yet committed)*
- **The Explorer filters through per-facet indexes** on datasets above 50 000 entries, so narrowing by level,
  environment or company no longer scans everything. *(working tree, not yet committed)*
- **Live watch rebuilds the grid's rows only when they change.** The source list was rebuilt — under the buffer
  lock — on *every* render, including renders caused by hovering or typing, and each one handed Radzen a new
  reference to re-filter, re-sort and re-page. *(working tree, not yet committed)*
- **The dashboard's volume chart is capped at 180 columns.** It plotted one column per hour over the whole
  span, so a month of logs was 720 SVG columns and a year was 8 760. *(working tree, not yet committed)*
- Level handling (WARN/WARNING, ERROR/FATAL) is centralised in `LogLevels`, which also removed the
  `ToUpperInvariant()` that the badges and chart colours allocated for every rendered row. Entry lists are
  pre-sized from the file size instead of always reserving room for a million entries.
  *(working tree, not yet committed)*
- Picking a file in the Live watch browser starts the watch immediately, instead of only filling the path box.
  *(working tree, not yet committed)*
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
