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
- Tells you when a new version has been released, and installs it for you

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

## Versioning

**The git tag is the only place a release version is written.** There is no version number to bump
in a file — pushing `v1.2.3` is what makes the release `1.2.3`.

From that one tag, the release workflow derives everything:

| Where the version ends up | How | Automatic? |
|---|---|---|
| The running app (sidebar footer) | `-p:InformationalVersion` (plus the commit, from SourceLink) | ✅ |
| Installer file name and Add/Remove Programs entry | `ISCC /DMyAppVersion` | ✅ |
| Portable ZIP file name and its `README.txt` | `create-portable-zip.ps1 -Version` | ✅ |
| Release title and SHA256 list | workflow | ✅ |
| **Release notes** — what changed in this version | the matching section of [`CHANGELOG.md`](CHANGELOG.md) | ✅ (but the section is yours to write — see step 1) |
| `winget/manifests/*.yaml` (`PackageVersion`) | — | ❌ **manual**, see step 4 below |

### Which version am I running?

Look at the bottom of the navigation sidebar — it shows `v1.2.3`. Click it to copy the full version
including the commit (`1.2.3+fe12a13c…`), which is what to paste into a bug report.

That value is read from the assembly at runtime, so it cannot go stale. A **local development build**
has no tag to take a version from and will show `1.0.0` (the `ApplicationDisplayVersion` fallback in
`LogAnalyzer.Maui.csproj`) with the commit you built from — which is exactly how you can tell a dev
build from a released one.

### Staying up to date

The desktop app checks for a newer release on its own. Once per run it asks GitHub for the latest
release of this repository, compares the tag with the version it is running, and — only if the tag is
newer — adds an **Update to v1.2.3** line above the version in the sidebar.

Clicking it shows the release notes and a **Download and install** button, which downloads the
release's `AlyCE-LogAnalyzer-Setup-{version}.exe`, checks it against the SHA256 published with the
release, and runs it silently. The app closes while it installs and starts again on the new version.
No admin rights are involved — the setup package is per-user, the same as a first install.

| Situation | What the app offers |
|---|---|
| Installed with `AlyCE-LogAnalyzer-Setup-*.exe` | Download and install, in place |
| Portable ZIP, or a build from source | The release page only — installing would leave the copy you are running behind, still on the old version, while the "update" landed in `%LOCALAPPDATA%` |
| Self-hosted web build (`LogAnalyzer`) | The release page only — that host is a published folder you replace |

A check that cannot complete — offline, behind a proxy, or past GitHub's anonymous rate limit —
shows nothing rather than an error, and is retried later in the session.

This is the app's **only outbound network request**. To switch it off, set:

```json
{ "LogAnalyzer": { "Updates": { "Enabled": false } } }
```

| Setting | Default | Meaning |
|---|---|---|
| `LogAnalyzer:Updates:Enabled` | `true` | Contact GitHub at all |
| `LogAnalyzer:Updates:Repository` | `gianpaoloi/AlyCE_LogAnalyzer` | Which repository's releases to read |
| `LogAnalyzer:Updates:CheckIntervalHours` | `6` | How long an answer is reused before asking again |

> Only versions tagged `vMAJOR.MINOR.PATCH` are offered, and comparison follows SemVer precedence:
> `1.10.0` beats `1.9.0`, and `1.3.0` beats `1.3.0-rc1`. Because GitHub's *latest release* excludes
> prereleases, a `v1.3.0-rc1` release is never offered as an update — deliberately, so a release
> candidate does not reach users who did not ask for one.

### Version numbers

Tags are `v<major>.<minor>.<patch>`, matching the workflow trigger `v*.*.*` — the leading `v` is
stripped for the version itself.

Note that `*` matches any character except `/`, so the trigger is looser than it looks: the trailing
`*` absorbs a suffix, and `v1.3.0-rc1` therefore **does** start a release. A tag whose version
contains a `-` is published as a GitHub *prerelease*; anything else is a full release.

| Tag | Result |
|---|---|
| `v1.2.3` | Release `1.2.3` |
| `v1.3.0-rc1` | **Pre**release `1.3.0-rc1` |
| `v1.2` | Matches the trigger, then **fails** the version step — needs all three parts |
| `v1.2.3.4` | Same — three parts exactly, not four |
| `release-1.2.3` | Ignored — needs the `v` prefix |

The workflow turns the tag into three separate values, because the MAUI SDK rejects a full SemVer
string in either of the properties it validates:

| Value | From `v1.3.0-rc1` | Used for |
|---|---|---|
| `VERSION` | `1.3.0-rc1` | `InformationalVersion`, so the sidebar shows the suffix; file names; release title |
| `CORE` | `1.3.0` | `ApplicationDisplayVersion`, which must be exactly `major.minor.patch` |
| `BUILD` | `10300` | `ApplicationVersion`, which must be a plain integer (`major*10000 + minor*100 + patch`) |

Because of that last column, a `minor` or `patch` above 99 fails the version step rather than
producing a build number that collides with a different release.

---

## Publishing a New Release

### 1. Get the code onto `main`

```bash
git checkout main
git pull
```

Everything you want in the release must be merged. The workflow **refuses to release a tag that is
not an ancestor of `main`** — a tag trigger fires wherever the tag was placed, so without that check
a version tag pushed from a feature branch would publish a release built from unmerged code.

Before tagging, it is worth running the gate locally so you don't discover a failure after the tag
exists:

```bash
dotnet test LogAnalyzer.Tests/LogAnalyzer.Tests.csproj
```

**Move anything still under *Unreleased* in [`CHANGELOG.md`](CHANGELOG.md) under a heading for the new
version, and commit that** — that section *becomes* the release notes, so this is the one manual step
that decides what users read on the release page:

```markdown
## [1.2.3] — 2026-09-01
```

The workflow looks for `## [1.2.3]` (the brackets and the trailing date are both optional). If it
finds nothing it falls back to *Unreleased*, and if that is empty too it just links to the file —
either way it logs a warning and **the release still publishes**, so a forgotten changelog costs you
a tidy release page, not a failed build.

### 2. Tag and push

```bash
git tag v1.2.3
git push origin v1.2.3
```

Pushing the **tag** is what starts the release. Pushing to `main` does not.

### 3. Watch the workflow

[`.github/workflows/release.yml`](.github/workflows/release.yml) then, in order:

1. checks the tag is on `main` — fails fast if not;
2. runs the unit tests — **a failure here publishes nothing**;
3. publishes the app **once**, self-contained win-x64;
4. packages that single output twice — portable ZIP, then Inno Setup installer — so the two
   downloads can never contain different binaries;
5. hashes both and uploads them as workflow artifacts (14-day retention), so they survive a failure
   in the release step itself;
6. assembles the release notes — the `CHANGELOG.md` section for this version, then the download table,
   the hashes and the system requirements. The assembled file is echoed into the step log, so you can
   see exactly what was published without opening the release;
7. creates the GitHub Release with both files attached:
   - `AlyCE-LogAnalyzer-Setup-1.2.3.exe` — the installer
   - `AlyCE-LogAnalyzer-1.2.3-win-x64.zip` — the portable package

   plus SHA256 for both in the release notes.

Then install the result and check the sidebar reads `v1.2.3` — that confirms the tag reached the
binary and not just the file names.

### 4. Update winget (only step that isn't automatic)

1. Take the installer URL and its **SHA256** from the release notes.
2. Update `PackageVersion` and those two values in the three files under
   [`winget/manifests/`](winget/manifests/) — all three must carry the same `PackageVersion`.
3. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs), copy the three files to
   `manifests/t/TeamSystem/AlyCELogAnalyzer/1.2.3/`, and open a Pull Request. The winget bot
   validates automatically.

### If something goes wrong

A tag can be replaced, but a *published* release is visible to users — prefer a new patch version
over rewriting one. To retry a tag that failed before anything was published:

```bash
git push --delete origin v1.2.3   # remove the remote tag
git tag -d v1.2.3                 # and the local one
# fix, commit to main, then tag again
```

To rehearse the whole thing, tag `v0.0.1-test` — it matches the trigger and, because the version
carries a `-`, publishes as a **prerelease** rather than as the latest release. Delete the tag and
the release afterwards.

---

## Projects

| Project | Target | Description |
|---|---|---|
| `LogAnalyzer.Maui` | `net10.0-windows10.0.19041.0` | **Main** — Windows desktop app (MAUI + Blazor Hybrid) |
| `LogAnalyzer.Core` | `net10.0` | Shared business logic and log parsing |
| `LogAnalyzer` | `net10.0` | Alternative Blazor Server variant (self-hosted web app) |
| `LogAnalyzer.Tests` | `net10.0` | Unit tests for `LogAnalyzer.Core` |

Everything is on .NET 10, so the .NET 10 SDK is the only prerequisite — building *and* running the tests
need nothing else installed.
