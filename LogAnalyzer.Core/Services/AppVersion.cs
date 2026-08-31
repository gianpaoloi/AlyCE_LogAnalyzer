using System.Reflection;

namespace LogAnalyzer.Services;

/// <summary>
/// The running build's version, read from the assembly rather than from any hand-maintained string.
/// <para>
/// The value comes from <see cref="AssemblyInformationalVersionAttribute"/>, which the SDK derives
/// from the project's version and — because SourceLink is on by default — suffixes with the exact
/// commit, giving something like <c>1.2.3+fe12a13c9ce22ebb…</c>. The release workflow stamps the tag
/// into it by passing <c>ApplicationDisplayVersion</c>, so a released build self-reports the version
/// it was released as and a bug report can be tied back to a commit.
/// </para>
/// <para>
/// This replaces a "Version: 1.0 / Build Date: 2026-07-16" block that was typed by hand into
/// README-HOW-TO.txt and displayed on the Quick Start page — it had been wrong for weeks, which is
/// what hand-maintained version strings always end up being.
/// </para>
/// </summary>
public static class AppVersion
{
    private const string Unknown = "unknown";
    private const int ShortCommitLength = 8;

    static AppVersion()
    {
        // The entry assembly is the application being run — the MAUI executable, or the web host.
        // That is the one carrying a real version; this library's own is never set by anything.
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;

        Full = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
               ?? assembly.GetName().Version?.ToString()
               ?? Unknown;

        (Display, Commit) = Split(Full);
        ShortCommit = Commit is not null ? Commit[..Math.Min(ShortCommitLength, Commit.Length)] : null;
    }

    /// <summary>Everything the assembly reports, e.g. <c>1.2.3+fe12a13c9ce22ebb…</c>.</summary>
    public static string Full { get; }

    /// <summary>Just the version, e.g. <c>1.2.3</c>, or <c>1.2.3-beta.1</c> for a prerelease.</summary>
    public static string Display { get; }

    /// <summary>The commit the build came from, when the build recorded one.</summary>
    public static string? Commit { get; }

    /// <summary>The leading characters of <see cref="Commit"/>, the way a commit is usually quoted.</summary>
    public static string? ShortCommit { get; }

    /// <summary>
    /// Splits an informational version into its version and build-metadata (commit) halves on the
    /// SemVer <c>+</c> separator. Public so the parsing can be tested without depending on the
    /// version of whatever assembly happens to be running the tests.
    /// </summary>
    public static (string Display, string? Commit) Split(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return (Unknown, null);

        var value = informationalVersion.Trim();
        var plus = value.IndexOf('+');
        if (plus < 0) return (value, null);

        var display = value[..plus];
        var metadata = value[(plus + 1)..];

        return (display.Length > 0 ? display : Unknown,
                metadata.Length > 0 ? metadata : null);
    }
}
