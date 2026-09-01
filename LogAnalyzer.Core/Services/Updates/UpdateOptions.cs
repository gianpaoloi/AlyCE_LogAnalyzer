using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace LogAnalyzer.Services.Updates;

/// <summary>
/// Settings for the update check, kept as a plain object rather than read from
/// <see cref="IConfiguration"/> inside the checker — that way the checker is constructible in a test
/// without a configuration root, and every knob has one obvious default.
/// </summary>
public sealed record UpdateOptions
{
    /// <summary>Configuration section the hosts read these from.</summary>
    public const string Section = "LogAnalyzer:Updates";

    /// <summary>This project's own repository — the only place a release is published.</summary>
    public const string DefaultRepository = "gianpaoloi/AlyCE_LogAnalyzer";

    /// <summary>
    /// Whether to contact GitHub at all. On by default, but settable to false because this is the
    /// app's only outbound network call and some environments will not want it made silently.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary><c>owner/name</c> of the repository whose releases are checked.</summary>
    public string Repository { get; init; } = DefaultRepository;

    /// <summary>
    /// How long a completed check is reused before the network is touched again. Generous because
    /// releases are rare and unauthenticated GitHub requests are rate limited per IP address; a
    /// manual check bypasses it.
    /// </summary>
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>How long to wait after a failed check before trying again.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>How long one request gets before it is abandoned.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The page a user is sent to when the app cannot install the update itself.</summary>
    public string ReleasesPageUrl => $"https://github.com/{Repository}/releases";

    /// <summary>
    /// Reads the <c>LogAnalyzer:Updates</c> section, falling back to the defaults above for anything
    /// absent or unparseable. Uses the indexer rather than the configuration binder, which is how the
    /// rest of this project reads settings and avoids depending on a package the MAUI host may not
    /// carry.
    /// </summary>
    public static UpdateOptions FromConfiguration(IConfiguration? configuration)
    {
        if (configuration is null) return new UpdateOptions();

        var defaults = new UpdateOptions();
        var repository = configuration[$"{Section}:Repository"];

        return new UpdateOptions
        {
            Enabled = bool.TryParse(configuration[$"{Section}:Enabled"], out var enabled)
                ? enabled
                : defaults.Enabled,
            Repository = string.IsNullOrWhiteSpace(repository) ? defaults.Repository : repository.Trim(),
            CheckInterval = ReadHours($"{Section}:CheckIntervalHours", defaults.CheckInterval),
        };

        // Invariant culture on purpose: a configuration file is not localised, and this app is
        // routinely run on an it-IT machine where the current culture would read "6.5" as 65.
        TimeSpan ReadHours(string key, TimeSpan fallback) =>
            double.TryParse(configuration[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours)
            && hours > 0
                ? TimeSpan.FromHours(hours)
                : fallback;
    }
}
