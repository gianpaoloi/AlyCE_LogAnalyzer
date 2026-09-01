<#
.SYNOPSIS
    Sets the app's version everywhere it is written down.

.DESCRIPTION
    A release version lives in five places in this repository, and they have to agree:

        LogAnalyzer.Maui/LogAnalyzer.Maui.csproj   ApplicationDisplayVersion + ApplicationVersion
        LogAnalyzer.Maui/installer/setup.iss       #define MyAppVersion (the fallback for a local iscc)
        winget/manifests/*.yaml                    PackageVersion, in all three manifests
        README.md                                  the version a local dev build is documented to show
        CHANGELOG.md                               the Unreleased section, cut into a released one

    The CHANGELOG matters more than it looks: the release workflow publishes that version's section
    verbatim as the GitHub release notes, so a version with no section ships an empty release.

    Two rules are copied from .github/workflows/release.yml on purpose, so that a local bump and a
    tagged CI build can never disagree:

        ApplicationVersion = major * 10000 + minor * 100 + patch
        minor and patch must each be <= 99, or that scheme silently collides with the next field up

    Nothing is written unless every edit is known to apply. Each file is matched, patched and
    verified in memory first; if any pattern does not match, the script reports all of them and
    exits without touching a single file. A half-applied version bump is worse than none.

    The script never pushes, and never creates a tag unless asked. See -Tag.

.PARAMETER Version
    The new version, with or without a leading "v": 1.2.3, v1.2.3, or 1.3.0-rc1.
    A prerelease suffix is kept for the installer, the manifests and the changelog heading, but is
    dropped from ApplicationDisplayVersion, which MAUI requires to be exactly major.minor.patch.

.PARAMETER Date
    The date for the new CHANGELOG heading. Defaults to today, formatted yyyy-MM-dd.

.PARAMETER NoChangelog
    Leave CHANGELOG.md alone and only update the version numbers.

.PARAMETER Tag
    Create the annotated git tag v<version> after a successful bump. Refuses to run if the working
    tree is dirty, because the tag has to point at a commit that already contains the new version.
    Never pushes - the push command is printed for you to run.

.PARAMETER Force
    Downgrade "pattern not found" from an error to a warning, and allow overwriting a CHANGELOG
    section that already exists for this version. Use when a file has been reworded and you intend
    to fix it by hand.

.EXAMPLE
    ./set-version.ps1 1.0.2

.EXAMPLE
    ./set-version.ps1 v1.1.0 -WhatIf
    Shows every file that would change, and what it would become, without writing anything.

.EXAMPLE
    ./set-version.ps1 1.1.0
    git add -A; git commit -m "Set version 1.1.0"
    ./set-version.ps1 1.1.0 -Tag
    Bump, commit, then tag the commit that contains the bump.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Version,

    [string] $Date = (Get-Date).ToString('yyyy-MM-dd'),

    [switch] $NoChangelog,

    [switch] $Tag,

    [switch] $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Written as a code point rather than a literal so this script's own encoding cannot corrupt the
# heading it produces - CHANGELOG.md uses an em dash, and the workflow parses those headings.
$emDash = [char]0x2014

# ---------------------------------------------------------------------------------------------
# Version parsing - the same rules the release workflow applies to a tag.
# ---------------------------------------------------------------------------------------------

$raw = $Version.Trim()
if ($raw.StartsWith('v') -or $raw.StartsWith('V')) { $raw = $raw.Substring(1) }

$match = [regex]::Match($raw, '^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?$')
if (-not $match.Success) {
    throw "Version '$Version' is not MAJOR.MINOR.PATCH[-suffix] (e.g. 1.2.3, v1.2.3, 1.3.0-rc1)."
}

$major = [int] $match.Groups[1].Value
$minor = [int] $match.Groups[2].Value
$patch = [int] $match.Groups[3].Value

if ($minor -gt 99 -or $patch -gt 99) {
    throw ("Version $raw does not fit the major*10000 + minor*100 + patch build-number scheme " +
           "that the release workflow uses; minor and patch must each be 99 or less.")
}

$full  = $raw                                        # 1.3.0-rc1 - installer, manifests, changelog
$core  = "$major.$minor.$patch"                      # 1.3.0     - ApplicationDisplayVersion
$build = $major * 10000 + $minor * 100 + $patch      # 10300     - ApplicationVersion

# ---------------------------------------------------------------------------------------------
# File helpers. Read and write bytes so the existing BOM is preserved and the existing line
# endings survive untouched - Set-Content would rewrite every line ending in the file.
# ---------------------------------------------------------------------------------------------

$root = $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }

function Get-RepoFile([string] $Relative) {
    $path = Join-Path $root $Relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Expected file not found: $Relative (is this script still at the repository root?)"
    }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    $hasBom = $text.Length -gt 0 -and $text[0] -eq [char]0xFEFF
    if ($hasBom) { $text = $text.Substring(1) }

    [pscustomobject]@{ Relative = $Relative; Path = $path; Text = $text; HasBom = $hasBom }
}

function Save-RepoFile($File, [string] $Text) {
    if ([string]::IsNullOrEmpty($Text)) {
        throw "Refusing to write an empty $($File.Relative)."
    }
    [System.IO.File]::WriteAllText($File.Path, $Text, [System.Text.UTF8Encoding]::new($File.HasBom))
}

# Every planned change lands here first; nothing is written until all of them are known good.
$planned = [System.Collections.Generic.List[object]]::new()
$problems = [System.Collections.Generic.List[string]]::new()

# Replaces exactly the capture group described by $Pattern's group 2, reporting rather than
# throwing so that all failures can be shown at once.
function Add-Edit($File, [string] $Label, [string] $Pattern, [string] $Replacement, [string] $Shows) {
    $matches = [regex]::Matches($File.Text, $Pattern)
    if ($matches.Count -eq 0) {
        $problems.Add("$($File.Relative): could not find $Label. Pattern: $Pattern")
        return
    }

    $before = ($matches | ForEach-Object { $_.Value.Trim() } | Select-Object -Unique) -join ' | '
    if ($before.Length -gt 40) { $before = $before.Substring(0, 39) + [char]0x2026 }
    $File.Text = [regex]::Replace($File.Text, $Pattern, $Replacement)

    $planned.Add([pscustomobject]@{
        File    = $File.Relative
        Setting = $Label
        Was     = $before
        Now     = $Shows
        Hits    = $matches.Count
    })
}

# ---------------------------------------------------------------------------------------------
# 1. The MAUI project: display version and build number.
# ---------------------------------------------------------------------------------------------

$csproj = Get-RepoFile 'LogAnalyzer.Maui/LogAnalyzer.Maui.csproj'

Add-Edit $csproj 'ApplicationDisplayVersion' `
    '(<ApplicationDisplayVersion>)[^<]*(</ApplicationDisplayVersion>)' `
    "`${1}$core`${2}" $core

Add-Edit $csproj 'ApplicationVersion' `
    '(<ApplicationVersion>)[^<]*(</ApplicationVersion>)' `
    "`${1}$build`${2}" "$build"

# ---------------------------------------------------------------------------------------------
# 2. Inno Setup: the fallback version a local `iscc setup.iss` uses. The workflow overrides this
#    with /DMyAppVersion=<full version>, so the fallback carries the suffix too.
# ---------------------------------------------------------------------------------------------

$iss = Get-RepoFile 'LogAnalyzer.Maui/installer/setup.iss'

Add-Edit $iss 'MyAppVersion' `
    '(?m)^(\s*#define\s+MyAppVersion\s+")[^"]*(")' `
    "`${1}$full`${2}" $full

# ---------------------------------------------------------------------------------------------
# 3. winget manifests - all three have to carry the same PackageVersion.
# ---------------------------------------------------------------------------------------------

$manifests = @(
    'winget/manifests/TeamSystem.AlyCELogAnalyzer.yaml'
    'winget/manifests/TeamSystem.AlyCELogAnalyzer.installer.yaml'
    'winget/manifests/TeamSystem.AlyCELogAnalyzer.locale.en-US.yaml'
) | ForEach-Object {
    $file = Get-RepoFile $_
    # [^\r\n]* rather than .*$ - "." matches "\r", so ".*$" in multiline mode consumes the CR of a
    # CRLF file and the replacement drops it, leaving one lone LF in an otherwise-CRLF file.
    Add-Edit $file 'PackageVersion' '(?m)^(PackageVersion:[ \t]*)[^\r\n]*' "`${1}$full" $full
    $file
}

# ---------------------------------------------------------------------------------------------
# 4. README: the version a local development build is documented to report, which is the
#    ApplicationDisplayVersion fallback and so tracks $core, not $full.
# ---------------------------------------------------------------------------------------------

$readme = Get-RepoFile 'README.md'

# Single-quoted, so the backticks are literal markdown, not PowerShell escapes.
Add-Edit $readme 'the documented dev-build version' `
    '(will show `)\d+\.\d+\.\d+(`)' `
    "`${1}$core`${2}" $core

# ---------------------------------------------------------------------------------------------
# 5. CHANGELOG: cut Unreleased into a released section. This is what the workflow publishes as
#    the release notes, so it is the part worth getting right.
# ---------------------------------------------------------------------------------------------

$changelog = $null
$notesWarning = $null

if (-not $NoChangelog) {
    $changelog = Get-RepoFile 'CHANGELOG.md'

    # Split on the file's own line endings so a CRLF checkout round-trips unchanged.
    $newline = if ($changelog.Text -match "`r`n") { "`r`n" } else { "`n" }
    $lines = $changelog.Text -split "`r?`n"

    $unreleasedAt = -1
    $nextHeadingAt = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^##\s+\[?Unreleased\]?') { $unreleasedAt = $i; continue }
        if ($unreleasedAt -ge 0 -and $lines[$i] -match '^##\s') { $nextHeadingAt = $i; break }
    }

    if ($unreleasedAt -lt 0) {
        $problems.Add('CHANGELOG.md: no "## [Unreleased]" heading to cut from.')
    }
    else {
        $existing = $lines | Where-Object { $_ -match "^##\s+\[?$([regex]::Escape($full))\]?" }
        if ($existing -and -not $Force) {
            $problems.Add("CHANGELOG.md: a section for $full already exists. Re-run with -Force to add another.")
        }

        $bodyEnd = if ($nextHeadingAt -ge 0) { $nextHeadingAt - 1 } else { $lines.Count - 1 }

        # Index-based trim: slicing with a range on a one-element array quietly returns the array
        # unchanged, which is an easy infinite loop in a while-based trim.
        $first = $unreleasedAt + 1
        $last = $bodyEnd
        while ($first -le $last -and [string]::IsNullOrWhiteSpace($lines[$first])) { $first++ }
        while ($last -ge $first -and [string]::IsNullOrWhiteSpace($lines[$last])) { $last-- }

        # Assigned in two statements on purpose: `$x = if (...) { @() }` assigns $null, because a
        # block that emits an empty array emits nothing at all.
        $body = @()
        if ($first -le $last) { $body = @($lines[$first..$last]) }

        $isPlaceholder = $body.Count -eq 0 -or ($body -join "`n").Trim() -eq '_Nothing yet._'

        if ($isPlaceholder) {
            $notesWarning = ("The Unreleased section is empty, so $full gets no release notes " +
                             "and the GitHub release body will be blank.")
            $body = @('_No changes recorded._')
        }

        $rebuilt = [System.Collections.Generic.List[string]]::new()
        if ($unreleasedAt -gt 0) { $rebuilt.AddRange([string[]] $lines[0..($unreleasedAt - 1)]) }
        $rebuilt.Add($lines[$unreleasedAt])          # keep the heading exactly as written
        $rebuilt.Add('')
        $rebuilt.Add('_Nothing yet._')
        $rebuilt.Add('')
        $rebuilt.Add("## [$full] $emDash $Date")
        $rebuilt.Add('')
        $rebuilt.AddRange([string[]] $body)
        if ($nextHeadingAt -ge 0) {
            $rebuilt.Add('')
            $rebuilt.AddRange([string[]] $lines[$nextHeadingAt..($lines.Count - 1)])
        }

        $changelog.Text = ($rebuilt -join $newline)

        $planned.Add([pscustomobject]@{
            File    = 'CHANGELOG.md'
            Setting = 'new section'
            Was     = 'under [Unreleased]'
            Now     = "[$full] $emDash $Date ($($body.Count) lines)"
            Hits    = 1
        })
    }
}

# ---------------------------------------------------------------------------------------------
# Report, then write - but only if every edit applied.
# ---------------------------------------------------------------------------------------------

if ($problems.Count -gt 0) {
    Write-Host ''
    Write-Host 'Some files could not be updated:' -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Red }
    Write-Host ''
    if (-not $Force) {
        throw "Nothing was written. Fix the files above, or re-run with -Force to skip them."
    }
    Write-Warning '-Force given: continuing with the edits that did apply. Check the files above by hand.'
}

Write-Host ''
Write-Host "Version $full  (display $core, build number $build)" -ForegroundColor Cyan
$planned | Format-Table -AutoSize File, Setting, Was, Now | Out-String | Write-Host

$files = @($csproj, $iss) + $manifests + @($readme)
if ($changelog) { $files += $changelog }

$written = 0
foreach ($file in $files) {
    if ($PSCmdlet.ShouldProcess($file.Relative, "set version to $full")) {
        Save-RepoFile $file $file.Text
        $written++
    }
}

if ($written -eq 0) {
    Write-Host 'Nothing written (-WhatIf).' -ForegroundColor Yellow
    return
}

Write-Host "Updated $written file(s)." -ForegroundColor Green

if ($notesWarning) {
    Write-Host ''
    Write-Warning $notesWarning
}

# ---------------------------------------------------------------------------------------------
# Optional tag. Guarded: the tag must point at a commit that already has the new version in it.
# ---------------------------------------------------------------------------------------------

if ($Tag) {
    $dirty = @(git status --porcelain)
    if ($dirty.Count -gt 0) {
        Write-Host ''
        Write-Warning ("Not tagging: the working tree has uncommitted changes, so v$full would point " +
                       "at a commit without the version bump. Commit first, then re-run with -Tag.")
    }
    elseif ($PSCmdlet.ShouldProcess("v$full", 'create annotated git tag')) {
        git tag -a "v$full" -m "AlyCE Log Analyzer $full"
        if ($LASTEXITCODE -ne 0) { throw "git tag failed for v$full." }
        Write-Host "Created local tag v$full (not pushed)." -ForegroundColor Green
    }
}

Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Cyan
Write-Host '  1. Write the release notes under the new CHANGELOG heading, if they are not there yet.'
Write-Host '  2. dotnet test LogAnalyzer.Tests/LogAnalyzer.Tests.csproj -c Release'
Write-Host "  3. dotnet msbuild LogAnalyzer.Maui/LogAnalyzer.Maui.csproj -getProperty:ApplicationDisplayVersion -getProperty:ApplicationVersion"
Write-Host "  4. git add -A; git commit -m `"Set version $full`""
if (-not $Tag) {
    Write-Host "  5. ./set-version.ps1 $full -Tag        # tags the commit you just made"
    Write-Host "  6. git push; git push origin v$full    # this publishes the release"
}
else {
    Write-Host "  5. git push; git push origin v$full    # this publishes the release"
}
Write-Host ''
Write-Host 'The winget manifests still carry {{INSTALLER_URL}} and {{SHA256}} placeholders; those are' -ForegroundColor DarkGray
Write-Host 'filled in from the release notes after the workflow has published the installer.' -ForegroundColor DarkGray
