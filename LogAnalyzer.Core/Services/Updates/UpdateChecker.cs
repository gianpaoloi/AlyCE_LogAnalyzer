using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LogAnalyzer.Services.Updates;

/// <summary>
/// Asks GitHub whether a newer release than the running build has been published.
/// <para>
/// Registered as a singleton so the answer is fetched once and shared: the sidebar asks on every
/// page, and every navigation would otherwise be a network request. Unauthenticated GitHub API calls
/// are rate limited to 60 per hour per IP address, which a busy afternoon of restarts could reach.
/// </para>
/// <para>
/// Nothing here throws for an unreachable or unhelpful GitHub. The check is a convenience in a log
/// viewer; a corporate proxy, an offline laptop or a rate-limit response must all end as a quiet
/// <see cref="UpdateStatus.Failed"/> that the UI simply does not show.
/// </para>
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    /// <summary>
    /// The API version header GitHub asks clients to pin, so a future breaking change to the release
    /// payload does not silently change what this parses.
    /// </summary>
    private const string ApiVersion = "2022-11-28";

    private readonly UpdateOptions _options;
    private readonly HttpClient _http;
    private readonly ReleaseVersion? _current;
    private readonly string _currentText;

    // One check at a time, and the result reused until it goes stale. A gate rather than a lock
    // because the work inside is asynchronous.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateCheckResult? _last;
    private DateTimeOffset _nextCheckAfter = DateTimeOffset.MinValue;

    /// <param name="options">Settings; the defaults are what the app ships with.</param>
    /// <param name="handler">
    /// Test seam. Left null in the app so the client owns a normal handler.
    /// </param>
    /// <param name="currentVersion">
    /// The version to compare against, defaulting to the running build's. Also a test seam: without
    /// it, a test's expectations would depend on the version of whatever assembly runs it.
    /// </param>
    public UpdateChecker(UpdateOptions? options = null,
                         HttpMessageHandler? handler = null,
                         string? currentVersion = null)
    {
        _options = options ?? new UpdateOptions();
        _currentText = currentVersion ?? AppVersion.Display;
        _current = ReleaseVersion.TryParse(_currentText, out var parsed) ? parsed : null;

        // disposeHandler: false for an injected handler — the caller that supplied it owns it.
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = _options.Timeout;

        // GitHub rejects requests without a User-Agent, and answers with the documented media type
        // only when asked for it.
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("AlyCE-LogAnalyzer", UserAgentVersion(_currentText)));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
    }

    /// <summary>The settings in force, so the UI can link to the right repository.</summary>
    public UpdateOptions Options => _options;

    /// <summary>The running build's version as displayed, even when it could not be parsed.</summary>
    public string CurrentVersion => _currentText;

    /// <summary>
    /// The most recent result, without triggering a check. Null until one has run — which is what
    /// lets a component render immediately and fill the answer in afterwards.
    /// </summary>
    public UpdateCheckResult? Last => _last;

    /// <summary>
    /// Returns what GitHub last said, checking first if the answer is stale or absent.
    /// </summary>
    /// <param name="force">
    /// Ignore the cached answer and go to the network — what the "Check now" button passes.
    /// </param>
    public async Task<UpdateCheckResult> CheckAsync(bool force = false,
                                                    CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return _last = UpdateCheckResult.Disabled;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force && _last is not null && DateTimeOffset.UtcNow < _nextCheckAfter) return _last;

            var result = await FetchAsync(cancellationToken).ConfigureAwait(false);

            // A failure is retried sooner than a success is refreshed: a laptop that was offline at
            // startup should not stay uninformed for the rest of the day.
            _nextCheckAfter = DateTimeOffset.UtcNow + (result.Status == UpdateStatus.Failed
                ? _options.RetryInterval
                : _options.CheckInterval);

            return _last = result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UpdateCheckResult> FetchAsync(CancellationToken cancellationToken)
    {
        if (_current is null)
        {
            // No point asking: whatever comes back could not be compared with this build.
            return new UpdateCheckResult(UpdateStatus.Failed, null,
                $"This build reports its version as '{_currentText}', which cannot be compared with a release tag.");
        }

        var url = $"https://api.github.com/repos/{_options.Repository}/releases/latest";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(UpdateStatus.Failed, null, Describe(response.StatusCode));

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var release = GitHubReleaseParser.Parse(json);

            if (release is null)
                return new UpdateCheckResult(UpdateStatus.Failed, null,
                    "GitHub's reply did not contain a release with a version tag.");

            return release.Version.IsNewerThan(_current)
                ? new UpdateCheckResult(UpdateStatus.UpdateAvailable, release)
                : new UpdateCheckResult(UpdateStatus.UpToDate, release);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up (page navigated away); that is not a failed check.
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient reports its own timeout as a cancellation with no token requested.
            return new UpdateCheckResult(UpdateStatus.Failed, null,
                $"GitHub did not answer within {_options.Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            return new UpdateCheckResult(UpdateStatus.Failed, null, $"Could not reach GitHub: {ex.Message}");
        }
        catch (JsonException)
        {
            return new UpdateCheckResult(UpdateStatus.Failed, null, "GitHub's reply could not be read.");
        }
    }

    private string Describe(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound =>
            $"{_options.Repository} has no published release yet.",
        // GitHub answers an exhausted unauthenticated quota with 403, not 429.
        HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests =>
            "GitHub's rate limit for anonymous requests has been reached; the check will run again later.",
        _ => $"GitHub answered {(int)status} {status}.",
    };

    /// <summary>
    /// Keeps only the characters valid in a User-Agent version token — the running version can be
    /// "unknown", or carry a suffix, and an invalid token would throw while building the client.
    /// </summary>
    private static string UserAgentVersion(string version)
    {
        var cleaned = new string(version.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-').ToArray());
        return cleaned.Length > 0 ? cleaned : "unknown";
    }

    public void Dispose()
    {
        // Safe even for an injected handler: the client was built with disposeHandler: false.
        _http.Dispose();
        _gate.Dispose();
    }
}
