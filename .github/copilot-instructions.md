# Copilot instructions — AlyCE Log Analyzer

Windows desktop app (.NET 10, MAUI + Blazor Hybrid) for loading, filtering, triaging and live-tailing structured (JSON-lines / Log4J XML / Grafana JSON) application logs. Logs stay on the machine, except text the user explicitly sends to an AI provider (`Services/Ai/`, the AI page linked from the top bar, and Triage → Explain). That send is always previewed first and goes through `AiRedactor`.

## Build, test, run

Prerequisites: .NET 10 SDK; MAUI workload (`dotnet workload install maui-windows`) only for the MAUI host.

```powershell
dotnet test LogAnalyzer.Tests/LogAnalyzer.Tests.csproj                       # all tests (xUnit)
dotnet test LogAnalyzer.Tests/LogAnalyzer.Tests.csproj --filter "FullyQualifiedName~LogParserTests"   # one class
dotnet test LogAnalyzer.Tests/LogAnalyzer.Tests.csproj --filter "FullyQualifiedName~LogParserTests.MethodName"  # one test

cd LogAnalyzer.Maui; dotnet run -f net10.0-windows10.0.19041.0               # main desktop app
dotnet run --project LogAnalyzer                                              # Blazor Server variant
```

There is no separate lint step. Tests need only the .NET 10 SDK (no MAUI workload). Packaging: `LogAnalyzer.Maui/create-release-package.ps1`, `create-portable-zip.ps1`, `installer/setup.iss` (Inno Setup).

## Architecture

Three projects around one shared library:

- **`LogAnalyzer.Core`** (Razor class library) — *everything*: `Models/`, `Services/` (parsing, store, watcher, exporters, Grafana import, updater) **and all Blazor pages/components** (`Components/Pages`: Overview/Home, Explorer, Triage, Live, Network, QuickStart…). Root namespace is `LogAnalyzer` (not `LogAnalyzer.Core`).
- **`LogAnalyzer.Maui`** — main host; `BlazorWebView` in-process (WebView2), no Kestrel. DI wired in `MauiProgram.cs`. Windows-only code: `WebView2Runtime.cs`, `Services/WindowsUpdateInstaller.cs` (implements `IUpdateInstaller`).
- **`LogAnalyzer`** — alternative Blazor Server host over the same Core components. Keep UI changes in Core so both hosts stay identical; host projects only do DI/bootstrapping and platform specifics.
- **`LogAnalyzer.Tests`** — references Core only; copies `Example-Logs/*.log` into the output as the real-world corpus for parser/signature tests.

Data flow: files/ZIP/Grafana JSON → `LogParser` / `Log4JXmlParser` (per-worker instances; not thread-safe) → `LogStore` holds one immutable `LogDataset` swapped wholesale when a load completes (so queries never see partial data; one load at a time). `LogWatcher` tails a file and `NetworkLogListener` receives NLogViewer UDP events; both feed `LiveEntryBuffer`. Triage groups by signature from `MessageNormalizer` (ids/GUIDs/paths/durations stripped).

DI lifetimes differ per host: `LogStore`, `LogWatcher`, `NetworkLogListener` are **singletons in MAUI** (dataset survives WebView reload, one UDP socket) but per-session in the web host. `SessionState`, `PathHistory`, `GrafanaPresetStore` are scoped (per WebView; the last two persist via localStorage). `UpdateChecker` is a singleton built by factory from `UpdateOptions`.

## Conventions

- Pages that react to `LogStore`/`LogWatcher` events derive from `ObservingComponentBase` and call `RequestRefresh(...)` — events fire on background threads, so never use `async void` handlers or call `StateHasChanged` directly from them.
- UI uses Radzen.Blazor (`material-dark` theme, remapped in `wwwroot/app.css`); keep it in sync across both hosts' `app.css`.
- Timestamps are parsed with the invariant culture and `RoundtripKind`; never use culture-dependent parsing.
- Parser interns low-cardinality strings (level, logger, environment) to save memory; progress notifications are throttled (`ProgressThrottleMs`) — avoid raising per-file UI events.
- Nullable and implicit usings are enabled everywhere; code comments explain *why* (history of a past bug), keep that style.

## Versioning & releases

- Version appears in five places that must agree; use `./set-version.ps1 <version>` (never edit by hand): Maui csproj, `installer/setup.iss`, three `winget/manifests/*.yaml`, README, CHANGELOG. `ApplicationVersion = major*10000 + minor*100 + patch` (minor/patch ≤ 99).
- Add user-facing changes to `CHANGELOG.md` under `## [Unreleased]`; the release workflow publishes the tagged version's section verbatim as release notes (an empty section ships empty notes).
- Releases are triggered by pushing a `vMAJOR.MINOR.PATCH[-suffix]` tag **that is on `main`** (`.github/workflows/release.yml`); it runs the tests first and won't publish if they fail. winget manifest update is a manual step (see README → Publishing a New Release).
