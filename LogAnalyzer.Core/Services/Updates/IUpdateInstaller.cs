namespace LogAnalyzer.Services.Updates;

/// <summary>How far a download has got, for the progress bar in the update dialog.</summary>
/// <param name="BytesReceived">Bytes written so far.</param>
/// <param name="TotalBytes">Total expected, when the server declared a length.</param>
public readonly record struct UpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    /// <summary>0..1, or null when the total is unknown — the bar then has to be indeterminate.</summary>
    public double? Fraction =>
        TotalBytes is > 0 ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1) : null;
}

/// <summary>
/// Applies an update to the running installation.
/// <para>
/// An interface because only one of the two hosts can do it: the MAUI desktop app is distributed as
/// a per-user Inno Setup package and can hand itself to a new copy of that installer, while the
/// self-hosted web build is a folder someone replaces. Both still want the *check* — hence this
/// split rather than putting the whole feature in the MAUI project.
/// </para>
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>
    /// True when <see cref="InstallAsync"/> can be expected to work. False for a portable or
    /// development build, and for the web host — the UI then offers the download page instead.
    /// </summary>
    bool CanInstall { get; }

    /// <summary>Why <see cref="CanInstall"/> is false, phrased for the user. Null when it is true.</summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// Downloads the release's installer, verifies it against the published hash, starts it and
    /// closes the app so it can be replaced. Returns only if something went wrong before the
    /// installer was started; otherwise the process is on its way out.
    /// </summary>
    Task InstallAsync(UpdateInfo update, IProgress<UpdateDownloadProgress>? progress,
                      CancellationToken cancellationToken = default);
}

/// <summary>
/// The installer for hosts that cannot update themselves. Registered by the web host, and used as
/// the fallback anywhere a platform implementation is absent, so the UI never has to null-check.
/// </summary>
public sealed class UnsupportedUpdateInstaller : IUpdateInstaller
{
    public UnsupportedUpdateInstaller(string reason) => UnavailableReason = reason;

    public bool CanInstall => false;

    public string? UnavailableReason { get; }

    public Task InstallAsync(UpdateInfo update, IProgress<UpdateDownloadProgress>? progress,
                             CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(UnavailableReason);
}
