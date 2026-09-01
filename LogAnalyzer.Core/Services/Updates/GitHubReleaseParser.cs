using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Services.Updates;

/// <summary>
/// Turns a GitHub <c>releases/latest</c> payload into an <see cref="UpdateInfo"/>.
/// <para>
/// Separate from <see cref="UpdateChecker"/> and free of I/O so the interesting half — which asset
/// counts as the installer, and where its hash comes from — is testable against a captured payload
/// instead of against the live API.
/// </para>
/// </summary>
public static partial class GitHubReleaseParser
{
    /// <summary>The <c>digest</c> field GitHub sets on an asset is prefixed with its algorithm.</summary>
    private const string DigestPrefix = "sha256:";

    private const int Sha256HexLength = 64;

    /// <summary>
    /// Returns null — rather than throwing — for a payload that is not a usable release: a draft, a
    /// tag that is not a version, or JSON that is not shaped like a release at all. The caller turns
    /// that into a failed check.
    /// </summary>
    public static UpdateInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var release = document.RootElement;
            if (release.ValueKind != JsonValueKind.Object) return null;

            // A draft has no public download, so it is not an update anyone can take.
            if (Bool(release, "draft")) return null;

            var tag = String(release, "tag_name");
            if (!ReleaseVersion.TryParse(tag, out var version)) return null;

            var notes = String(release, "body");
            var installer = SelectInstaller(release);

            return new UpdateInfo
            {
                Version = version,
                TagName = tag!,
                // html_url is normally present; the releases page is a usable stand-in if it is not.
                ReleaseUrl = String(release, "html_url") ?? $"https://github.com/releases/tag/{tag}",
                Title = String(release, "name"),
                Notes = notes,
                PublishedAt = Date(release, "published_at") ?? Date(release, "created_at"),
                IsPrerelease = Bool(release, "prerelease"),
                InstallerName = installer.Name,
                InstallerUrl = installer.Url,
                InstallerSize = installer.Size,
                // The asset's own digest is authoritative; the hash block in the notes is the
                // fallback for releases published before GitHub started returning digests.
                InstallerSha256 = installer.Sha256 ?? Sha256FromNotes(notes, installer.Name),
            };
        }
    }

    /// <summary>
    /// Picks the asset to install from. Only <c>.exe</c> assets qualify — the portable ZIP cannot
    /// update an installed copy — and a name containing "setup" wins, so an unrelated executable
    /// attached to a release cannot be mistaken for the installer.
    /// </summary>
    private static (string? Name, string? Url, long? Size, string? Sha256) SelectInstaller(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return default;

        (string? Name, string? Url, long? Size, string? Sha256) best = default;
        var bestIsSetup = false;

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;

            var name = String(asset, "name");
            var url = String(asset, "browser_download_url");
            if (name is null || url is null) continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            var isSetup = name.Contains("setup", StringComparison.OrdinalIgnoreCase);
            if (best.Url is not null && (bestIsSetup || !isSetup)) continue;

            best = (name, url, Number(asset, "size"), DigestSha256(asset));
            bestIsSetup = isSetup;
        }

        return best;
    }

    private static string? DigestSha256(JsonElement asset)
    {
        var digest = String(asset, "digest");
        if (digest is null || !digest.StartsWith(DigestPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var hex = digest[DigestPrefix.Length..].Trim();
        return hex.Length == Sha256HexLength ? hex : null;
    }

    /// <summary>
    /// Finds the installer's hash in the release notes. The release workflow writes a
    /// <c>&lt;hash&gt;  &lt;file name&gt;</c> block, the format <c>sha256sum</c> uses, so the file
    /// name has to match the asset — a release attaching several files publishes several hashes.
    /// </summary>
    private static string? Sha256FromNotes(string? notes, string? assetName)
    {
        if (string.IsNullOrWhiteSpace(notes) || string.IsNullOrWhiteSpace(assetName)) return null;

        foreach (var match in HashLine().Matches(notes).Cast<Match>())
        {
            if (string.Equals(match.Groups[2].Value, assetName, StringComparison.OrdinalIgnoreCase))
                return match.Groups[1].Value;
        }

        return null;
    }

    [GeneratedRegex(@"^[ \t]*([0-9a-fA-F]{64})[ \t]+(\S+)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex HashLine();

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? Trimmed(value.GetString())
            : null;

    private static string? Trimmed(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : null;

    private static DateTimeOffset? Date(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.TryGetDateTimeOffset(out var date)
            ? date
            : null;
}
