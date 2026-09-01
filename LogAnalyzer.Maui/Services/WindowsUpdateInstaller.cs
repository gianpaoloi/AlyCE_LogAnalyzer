using System.Diagnostics;
using System.Security.Cryptography;
using LogAnalyzer.Services.Updates;

namespace LogAnalyzer.Maui.Services;

/// <summary>
/// Updates an installed copy of the desktop app by downloading the release's Inno Setup package and
/// handing over to it.
/// <para>
/// There is no in-place file replacement here, and deliberately so: the app is running from the
/// directory that has to be overwritten, and the setup package already knows how to do that, how to
/// keep the Start-menu entries, and how to add the WebView2 runtime. Re-running it with
/// <c>/SILENT</c> is a supported upgrade path — the install is per-user, so no elevation is involved
/// either.
/// </para>
/// <para>
/// The one thing the setup package needed for this is the <c>/UPDATED=1</c> switch: a silent install
/// skips the "launch the app" step, so without it the app would download an update, close, and never
/// come back. See <c>installer/setup.iss</c>.
/// </para>
/// </summary>
public sealed class WindowsUpdateInstaller : IUpdateInstaller
{
    /// <summary>
    /// <c>/SILENT</c> shows only a progress window — not <c>/VERYSILENT</c>, because a self-update
    /// that gives no sign of running looks like a crash. <c>/NORESTARTAPPLICATIONS</c> leaves
    /// restarting the app to the <c>/UPDATED=1</c> entry, so it cannot be started twice.
    /// </summary>
    private const string InstallerArguments =
        "/SILENT /NOCANCEL /NORESTART /NORESTARTAPPLICATIONS /UPDATED=1";

    /// <summary>Big enough that the progress callback is not the bottleneck on a fast connection.</summary>
    private const int DownloadBufferSize = 128 * 1024;

    /// <summary>
    /// Long: this is an ~80 MB self-contained package, possibly over a corporate link. The user is
    /// watching a progress bar and can cancel.
    /// </summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Cleared before each download rather than after the install — the process is gone by then, and
    /// leaving the last installer behind is also what makes a failed update diagnosable.
    /// </summary>
    private static string DownloadDirectory =>
        Path.Combine(Path.GetTempPath(), "AlyCE-LogAnalyzer-update");

    public WindowsUpdateInstaller()
    {
        // Evaluated once, at startup: the answer cannot change while the app runs, and the UI asks
        // for it on every render of the update dialog.
        UnavailableReason = InstallLocation.IsSetupInstall(AppContext.BaseDirectory)
            ? null
            : "This copy was not installed by the AlyCE Log Analyzer setup — it is a portable or " +
              "development build. Installing over it would leave this copy behind, still on the old " +
              "version, so download the new one from GitHub instead.";
    }

    public bool CanInstall => UnavailableReason is null;

    public string? UnavailableReason { get; }

    public async Task InstallAsync(UpdateInfo update, IProgress<UpdateDownloadProgress>? progress,
                                   CancellationToken cancellationToken = default)
    {
        if (!CanInstall) throw new InvalidOperationException(UnavailableReason);

        if (!update.HasInstaller)
            throw new InvalidOperationException(
                $"Release {update.TagName} has no installer attached, so it cannot be installed automatically.");

        // The URL came from a JSON document off the network. It decides what gets executed in a
        // moment, so it is checked rather than trusted.
        if (!UpdateDownload.IsTrustedUrl(update.InstallerUrl))
            throw new InvalidOperationException(
                $"The installer for {update.TagName} is not hosted on GitHub ({update.InstallerUrl}); " +
                "it will not be downloaded.");

        var file = await DownloadAsync(update, progress, cancellationToken).ConfigureAwait(false);

        await VerifyAsync(file, update.InstallerSha256, cancellationToken).ConfigureAwait(false);

        await StartAsync(file, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> DownloadAsync(UpdateInfo update,
                                                    IProgress<UpdateDownloadProgress>? progress,
                                                    CancellationToken cancellationToken)
    {
        var directory = DownloadDirectory;
        TryClear(directory);
        Directory.CreateDirectory(directory);

        var file = Path.Combine(directory, UpdateDownload.SafeFileName(update.InstallerName));

        using var http = new HttpClient { Timeout = DownloadTimeout };
        using var response = await http
            .GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        // Content-Length is normally present; fall back to the size the API reported so the progress
        // bar still has a total.
        var total = response.Content.Headers.ContentLength ?? update.InstallerSize;

        try
        {
            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new FileStream(file, FileMode.Create, FileAccess.Write,
                                                         FileShare.None, DownloadBufferSize, useAsync: true);

            var buffer = new byte[DownloadBufferSize];
            long received = 0;
            int read;

            progress?.Report(new UpdateDownloadProgress(0, total));

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                progress?.Report(new UpdateDownloadProgress(received, total));
            }
        }
        catch
        {
            // A half-written installer must not be left where the next attempt might run it.
            TryDelete(file);
            throw;
        }

        return file;
    }

    /// <summary>
    /// Checks the download against the hash published with the release. Skipped only when the
    /// release published none — the transport was still HTTPS to GitHub in that case, which is the
    /// same assurance a user clicking the download link gets.
    /// </summary>
    private static async Task VerifyAsync(string file, string? expected, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expected)) return;

        string actual;
        await using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                                                 DownloadBufferSize, useAsync: true))
        {
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            actual = Convert.ToHexString(hash);
        }

        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return;

        TryDelete(file);

        throw new InvalidDataException(
            "The downloaded installer does not match the SHA256 published with the release, so it " +
            $"has been deleted rather than run.{Environment.NewLine}" +
            $"Expected {expected}{Environment.NewLine}Received {actual}");
    }

    /// <summary>
    /// How long to give the installer to fail before assuming it is running. Long enough to catch an
    /// immediate refusal — blocked by policy, or a corrupt download — and short enough not to look
    /// like a hang. A user still deciding at a SmartScreen prompt keeps the process alive, so this
    /// does not wait for them.
    /// </summary>
    private static readonly TimeSpan FailFastWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Starts the installer and closes the app, because the installer has to replace this
    /// executable. Setup restarts the new build afterwards, via <c>/UPDATED=1</c>.
    /// </summary>
    private static async Task StartAsync(string file, CancellationToken cancellationToken)
    {
        using var setup = Process.Start(new ProcessStartInfo(file)
        {
            Arguments = InstallerArguments,
            // Required for a .exe to be launched by the shell; also what surfaces a SmartScreen
            // prompt to the user rather than failing silently.
            UseShellExecute = true,
        }) ?? throw new InvalidOperationException($"The installer at {file} could not be started.");

        // Quitting the app is not undoable, so give an installer that refuses to run a moment to say
        // so — otherwise the app would simply vanish and come back still on the old version, with
        // nothing on screen to explain why.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FailFastWindow);

        try
        {
            await setup.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            // It exited already, so it never got as far as installing anything.
            throw new InvalidOperationException(
                $"The installer exited immediately with code {setup.ExitCode} without installing the update.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Still running after the window elapsed, which is the normal case.
        }

        // Quit rather than Environment.Exit so MAUI shuts the window and the WebView down cleanly.
        // Setup also closes applications still holding files in the install directory, so a slow
        // exit delays the update rather than breaking it.
        MainThread.BeginInvokeOnMainThread(() => Application.Current?.Quit());
    }

    private static void TryClear(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left over from a previous attempt and still locked; the download below overwrites the
            // file it needs anyway.
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do — the caller is already reporting a failure.
        }
    }
}
