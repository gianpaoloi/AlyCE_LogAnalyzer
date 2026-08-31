using System.Globalization;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>
/// Parses a single JSON-lines log record from UTF-8 bytes. Tolerant of missing optional fields and
/// interns low-cardinality strings (level, logger, environment…) to keep memory down.
/// <para>
/// One instance is not thread-safe (the intern pool isn't); the loader gives each worker its own.
/// </para>
/// </summary>
public sealed class LogParser
{
    /// <summary>
    /// Accepted timestamp shapes, tried in this order. Always parsed with the invariant culture:
    /// the fallback used to be a bare <c>DateTime.TryParse</c>, so the same log file parsed
    /// differently — or not at all — depending on the machine's regional settings.
    /// </summary>
    private static readonly string[] TimeFormats =
    {
        "yyyy-MM-dd HH:mm:ss.ffff",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss.ff",
        "yyyy-MM-dd HH:mm:ss.f",
        "yyyy-MM-dd HH:mm:ss",
        // ISO-8601, which is what most other emitters produce.
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
        "yyyy-MM-ddTHH:mm:ssK",
    };

    /// <summary>
    /// <see cref="DateTimeStyles.RoundtripKind"/> so an ISO timestamp carrying <c>Z</c> or an
    /// offset keeps it, rather than being silently shifted into the reader machine's local zone.
    /// </summary>
    private const DateTimeStyles TimeStyles =
        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind;

    // Property names as UTF-8, so matching never has to allocate a string for the name.
    private static readonly byte[] TimeName = "time"u8.ToArray();
    private static readonly byte[] LevelName = "level"u8.ToArray();
    private static readonly byte[] ThreadIdName = "threadid"u8.ToArray();
    private static readonly byte[] EnvironmentName = "environment"u8.ToArray();
    private static readonly byte[] UsernameName = "username"u8.ToArray();
    private static readonly byte[] CompanyName = "company"u8.ToArray();
    private static readonly byte[] CidName = "cid"u8.ToArray();
    private static readonly byte[] MessageName = "message"u8.ToArray();
    private static readonly byte[] LoggerName = "logger"u8.ToArray();

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    // Intern pool shared across a load so repeated tenant/level/logger strings reuse one instance.
    private readonly Dictionary<string, string> _intern = new(StringComparer.Ordinal);

    /// <summary>
    /// Cap on that pool. Interning pays off for the values that repeat — level, logger, environment,
    /// thread — and those turn up in the first lines; high-cardinality fields such as username or cid
    /// would otherwise grow the pool for as long as parsing continues. That is unbounded for
    /// <see cref="LogWatcher"/>, which keeps one parser for the whole life of the app: on a long watch
    /// the pool held every distinct value ever seen, and the growth eventually stalls the app.
    /// </summary>
    private const int MaxInterned = 20_000;

    private string? Intern(string? s)
    {
        if (s is null) return null;
        if (_intern.TryGetValue(s, out var existing)) return existing;
        // Past the cap the string is used as-is: a duplicate instance costs less than an endless pool.
        if (_intern.Count < MaxInterned) _intern[s] = s;
        return s;
    }

    /// <summary>Returns null when the line is blank or not valid JSON.</summary>
    public LogEntry? TryParse(ReadOnlySpan<byte> utf8Line, string sourceFile)
    {
        // Fast reject of blank and obviously non-JSON lines, before the reader is even built.
        var trimmed = TrimStart(utf8Line);
        if (trimmed.IsEmpty || trimmed[0] != (byte)'{') return null;

        string? time = null, level = null, threadId = null, environment = null,
                username = null, company = null, cid = null, message = null, logger = null;

        try
        {
            var reader = new Utf8JsonReader(trimmed, ReaderOptions);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;

            // One pass over the line. The old code built a JsonDocument and then did nine
            // TryGetProperty lookups, each of which re-walked the property list.
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals(TimeName)) time = ReadString(ref reader);
                else if (reader.ValueTextEquals(LevelName)) level = ReadString(ref reader);
                else if (reader.ValueTextEquals(MessageName)) message = ReadString(ref reader);
                else if (reader.ValueTextEquals(LoggerName)) logger = ReadString(ref reader);
                else if (reader.ValueTextEquals(ThreadIdName)) threadId = ReadString(ref reader);
                else if (reader.ValueTextEquals(EnvironmentName)) environment = ReadString(ref reader);
                else if (reader.ValueTextEquals(UsernameName)) username = ReadString(ref reader);
                else if (reader.ValueTextEquals(CompanyName)) company = ReadString(ref reader);
                else if (reader.ValueTextEquals(CidName)) cid = ReadString(ref reader);
                else Skip(ref reader);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return new LogEntry
        {
            Time = ParseTime(time),
            Level = Intern(level) ?? "UNKNOWN",
            ThreadId = Intern(threadId) ?? "",
            Environment = Intern(NullIfEmpty(environment)),
            Username = Intern(NullIfEmpty(username)),
            Company = Intern(NullIfEmpty(company)),
            Cid = Intern(NullIfEmpty(cid)),
            Message = message ?? "",
            Logger = Intern(logger) ?? "",
            SourceFile = sourceFile,
        };
    }

    /// <summary>Convenience overload for callers that already hold a string (tests, ad-hoc parsing).</summary>
    public LogEntry? TryParse(string line, string sourceFile) =>
        TryParse(Encoding.UTF8.GetBytes(line), sourceFile);

    /// <summary>Reads a string value, or skips a value of any other kind and returns null.</summary>
    private static string? ReadString(ref Utf8JsonReader reader)
    {
        if (!reader.Read()) return null;
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();
        Skip(ref reader);
        return null;
    }

    /// <summary>Steps over the value of the current property, nested objects and arrays included.</summary>
    private static void Skip(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.PropertyName && !reader.Read()) return;
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
    }

    private static ReadOnlySpan<byte> TrimStart(ReadOnlySpan<byte> line)
    {
        var i = 0;
        while (i < line.Length && line[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') i++;
        return line[i..];
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static DateTime ParseTime(string? s)
    {
        if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
        if (DateTime.TryParseExact(s, TimeFormats, CultureInfo.InvariantCulture, TimeStyles, out var dt))
            return dt;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, TimeStyles, out dt) ? dt : DateTime.MinValue;
    }
}
