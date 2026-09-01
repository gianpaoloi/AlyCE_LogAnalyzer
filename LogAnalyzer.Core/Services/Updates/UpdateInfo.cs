namespace LogAnalyzer.Services.Updates;

/// <summary>A published release, reduced to the parts the app actually uses.</summary>
public sealed record UpdateInfo
{
    /// <summary>Parsed from the tag, so it can be ordered against the running build.</summary>
    public required ReleaseVersion Version { get; init; }

    /// <summary>The tag the release was cut from, e.g. <c>v1.2.3</c>.</summary>
    public required string TagName { get; init; }

    /// <summary>The release page, which is where a user is sent when the app cannot update itself.</summary>
    public required string ReleaseUrl { get; init; }

    /// <summary>The release title, when it has one distinct from the tag.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// The release notes, as the Markdown GitHub stores. Shown as text — the app has no Markdown
    /// renderer, and the notes this project publishes read acceptably either way.
    /// </summary>
    public string? Notes { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>True when GitHub marked the release as a prerelease.</summary>
    public bool IsPrerelease { get; init; }

    /// <summary>File name of the installer asset, e.g. <c>AlyCE-LogAnalyzer-Setup-1.2.3.exe</c>.</summary>
    public string? InstallerName { get; init; }

    /// <summary>Direct download URL for the installer asset.</summary>
    public string? InstallerUrl { get; init; }

    /// <summary>Asset size in bytes, so the dialog can say how much it is about to download.</summary>
    public long? InstallerSize { get; init; }

    /// <summary>
    /// Expected SHA256 of the installer, when the release publishes one — from the asset's own
    /// digest field, or failing that from the hash block the release workflow writes into the notes.
    /// The download is checked against it before anything is executed.
    /// </summary>
    public string? InstallerSha256 { get; init; }

    /// <summary>False for a release that published no installer — nothing to install from.</summary>
    public bool HasInstaller => !string.IsNullOrWhiteSpace(InstallerUrl);
}

/// <summary>Outcome of one update check.</summary>
public enum UpdateStatus
{
    /// <summary>Checking is switched off by configuration; no request was made.</summary>
    Disabled,

    /// <summary>The newest release is not newer than the running build.</summary>
    UpToDate,

    /// <summary>A newer release exists.</summary>
    UpdateAvailable,

    /// <summary>The check could not be completed. <see cref="UpdateCheckResult.Message"/> says why.</summary>
    Failed,
}

/// <summary>
/// What a check found. A failure is a result rather than an exception: an update check is a
/// convenience running in the background of a log viewer, and must never surface as an error the
/// user has to dismiss.
/// </summary>
/// <param name="Status">What the check concluded.</param>
/// <param name="Release">
/// The newest release, when one was read — present for both <see cref="UpdateStatus.UpToDate"/> and
/// <see cref="UpdateStatus.UpdateAvailable"/>, so the dialog can show what it compared against.
/// </param>
/// <param name="Message">Human-readable reason, set when <paramref name="Status"/> is a failure.</param>
public sealed record UpdateCheckResult(UpdateStatus Status, UpdateInfo? Release = null, string? Message = null)
{
    public static readonly UpdateCheckResult Disabled = new(UpdateStatus.Disabled);

    /// <summary>True only when there is a newer release *and* its details were read.</summary>
    public bool IsUpdateAvailable => Status == UpdateStatus.UpdateAvailable && Release is not null;
}
