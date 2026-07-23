# Publishing AlyCE Log Analyzer to Winget

This document describes how to publish and update the **AlyCE Log Analyzer** package in the [Windows Package Manager Community Repository](https://github.com/microsoft/winget-pkgs).

**Package ID:** `TeamSystem.AlyCELogAnalyzer`  
**Install command:** `winget install TeamSystem.AlyCELogAnalyzer`

---

## Prerequisites

- The GitHub repository must be **public**
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) (for local builds only — CI installs it automatically)
- A GitHub account to submit PRs to `microsoft/winget-pkgs`

---

## Step-by-step: Publishing a New Version

### 1 — Push a version tag

The GitHub Actions release workflow (`release.yml`) triggers automatically on tags matching `v*.*.*`.

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The workflow will:
1. Build the MAUI app (`dotnet publish`, self-contained, win-x64, Release)
2. Compile the Inno Setup installer → `AlyCE-LogAnalyzer-Setup-{version}.exe`
3. Compute the SHA256 hash of the installer
4. Create a **GitHub Release** with:
   - The installer `.exe` as a downloadable asset
   - The SHA256 hash printed in the Release notes

### 2 — Note the installer URL and SHA256

After the GitHub Release is created, collect these two values from the Release page:

| Value | Where to find it |
|---|---|
| **Installer URL** | Direct link to the `.exe` asset, e.g. `https://github.com/<org>/AlyCE_LogAnalyzer/releases/download/v1.0.0/AlyCE-LogAnalyzer-Setup-1.0.0.exe` |
| **SHA256** | Printed in the Release notes body |

### 3 — Update the winget manifest files

Edit `winget/manifests/TeamSystem.AlyCELogAnalyzer.installer.yaml`:

- Replace `{{INSTALLER_URL}}` with the direct download URL of the `.exe`
- Replace `{{SHA256}}` with the SHA256 hash from the Release notes

Also update `PackageVersion` in all three manifest files if this is a new version:

- `winget/manifests/TeamSystem.AlyCELogAnalyzer.yaml`
- `winget/manifests/TeamSystem.AlyCELogAnalyzer.installer.yaml`
- `winget/manifests/TeamSystem.AlyCELogAnalyzer.locale.en-US.yaml`

### 4 — Submit to microsoft/winget-pkgs

1. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)

2. Create the manifest folder at the correct path:
   ```
   manifests/t/TeamSystem/AlyCELogAnalyzer/{version}/
   ```
   Example for v1.0.0:
   ```
   manifests/t/TeamSystem/AlyCELogAnalyzer/1.0.0/
   ```

3. Copy the three YAML files from `winget/manifests/` into that folder:
   ```
   TeamSystem.AlyCELogAnalyzer.yaml
   TeamSystem.AlyCELogAnalyzer.installer.yaml
   TeamSystem.AlyCELogAnalyzer.locale.en-US.yaml
   ```

4. Open a Pull Request against `microsoft/winget-pkgs` — the winget validation bot (`@wingetbot`) will run automated checks.

5. Once approved and merged, the package is live:
   ```
   winget install TeamSystem.AlyCELogAnalyzer
   ```

---

## Winget Manifest Reference

The three manifest files are stored in this repository under `winget/manifests/` as templates.

| File | Type | Purpose |
|---|---|---|
| `TeamSystem.AlyCELogAnalyzer.yaml` | `version` | Declares the package version |
| `TeamSystem.AlyCELogAnalyzer.installer.yaml` | `installer` | Installer URL, SHA256, architecture, install switches |
| `TeamSystem.AlyCELogAnalyzer.locale.en-US.yaml` | `defaultLocale` | App name, description, publisher, tags |

### Installer silent switches (Inno Setup defaults)

| Mode | Switch |
|---|---|
| Silent (no UI) | `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` |
| Silent with progress | `/SILENT /SUPPRESSMSGBOXES /NORESTART /SP-` |

> **Install scope:** per-user — installs to `%LOCALAPPDATA%\TeamSystem\AlyCE Log Analyzer`. No admin / UAC prompt required.

---

## Local Installer Build (without CI)

```powershell
# 1. Publish the MAUI app
dotnet publish LogAnalyzer.Maui/LogAnalyzer.Maui.csproj `
    -c Release `
    -f net10.0-windows10.0.19041.0 `
    --runtime win-x64 `
    --self-contained

# 2. Compile the installer (adjust version as needed)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
    /DMyAppVersion=1.0.0 `
    LogAnalyzer.Maui/installer/setup.iss

# Output: LogAnalyzer.Maui/dist/AlyCE-LogAnalyzer-Setup-1.0.0.exe

# 3. Compute SHA256
(Get-FileHash "LogAnalyzer.Maui/dist/AlyCE-LogAnalyzer-Setup-1.0.0.exe" -Algorithm SHA256).Hash
```

---

## Useful Links

- [winget-pkgs repository](https://github.com/microsoft/winget-pkgs)
- [Winget manifest schema docs](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest)
- [wingetcreate tool](https://github.com/microsoft/winget-create) — can auto-generate manifests from an installer URL
- [Inno Setup documentation](https://jrsoftware.org/ishelp/)
