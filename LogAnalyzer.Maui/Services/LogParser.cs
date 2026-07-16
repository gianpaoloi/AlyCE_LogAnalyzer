using System.Text.Json;
using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>
/// Parses a single JSON-lines log record. Tolerant of missing optional fields and
/// interns low-cardinality strings (level, logger, environment…) to keep memory down.
/// </summary>
public sealed class LogParser
{
    private static readonly string[] TimeFormats =
    {
        "yyyy-MM-dd HH:mm:ss.ffff",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss.ff",
        "yyyy-MM-dd HH:mm:ss.f",
        "yyyy-MM-dd HH:mm:ss",
    };

    // Intern pool shared across a load so repeated tenant/level/logger strings reuse one instance.
    private readonly Dictionary<string, string> _intern = new(StringComparer.Ordinal);

    private string? Intern(string? s)
    {
        if (s is null) return null;
        if (_intern.TryGetValue(s, out var existing)) return existing;
        _intern[s] = s;
        return s;
    }

    /// <summary>Returns null when the line is blank or not valid JSON.</summary>
    public LogEntry? TryParse(ReadOnlySpan<char> line, string sourceFile)
    {
        if (line.IsWhiteSpace()) return null;
        // Fast reject of obviously non-JSON lines.
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return null;

        try
        {
            using var doc = JsonDocument.Parse(line.ToString());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var timeStr = GetString(root, "time");
            var time = ParseTime(timeStr);

            return new LogEntry
            {
                Time = time,
                Level = Intern(GetString(root, "level")) ?? "UNKNOWN",
                ThreadId = Intern(GetString(root, "threadid")) ?? "",
                Environment = Intern(NullIfEmpty(GetString(root, "environment"))),
                Username = Intern(NullIfEmpty(GetString(root, "username"))),
                Company = Intern(NullIfEmpty(GetString(root, "company"))),
                Cid = Intern(NullIfEmpty(GetString(root, "cid"))),
                Message = GetString(root, "message") ?? "",
                Logger = Intern(GetString(root, "logger")) ?? "",
                SourceFile = sourceFile,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? GetString(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static DateTime ParseTime(string? s)
    {
        if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
        if (DateTime.TryParseExact(s, TimeFormats, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return dt;
        return DateTime.TryParse(s, out dt) ? dt : DateTime.MinValue;
    }
}
