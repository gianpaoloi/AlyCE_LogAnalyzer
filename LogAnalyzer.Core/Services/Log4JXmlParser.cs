using System.Globalization;
using System.Xml;
using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>
/// Parses one <c>&lt;log4j:event&gt;</c> record — the wire format of NLog's <c>NLogViewer</c>
/// target (<c>Log4JXmlEventLayout</c>) — into a <see cref="LogEntry"/>, so events arriving over the
/// network end up in the same grid, filters and export as lines read from a file.
/// <para>
/// See <see href="https://github.com/NLog/NLog/wiki/NLogViewer-target"/>. A typical event:
/// </para>
/// <code>
/// &lt;log4j:event logger="My.Logger" level="WARN" timestamp="1790000000000" thread="12"&gt;
///   &lt;log4j:message&gt;Something went wrong&lt;/log4j:message&gt;
///   &lt;log4j:throwable&gt;System.InvalidOperationException: ...&lt;/log4j:throwable&gt;
///   &lt;log4j:properties&gt;
///     &lt;log4j:data name="log4japp" value="MyApp(1234)" /&gt;
///     &lt;log4j:data name="log4jmachinename" value="HOST01" /&gt;
///   &lt;/log4j:properties&gt;
/// &lt;/log4j:event&gt;
/// </code>
/// <para>
/// One instance is not thread-safe (the intern pool isn't); the listener uses a single one from its
/// receive loop, exactly as <see cref="LogWatcher"/> does with <see cref="LogParser"/>.
/// </para>
/// </summary>
public sealed class Log4JXmlParser
{
    /// <summary>
    /// The event as sent is a *fragment*, and its <c>log4j:</c> / <c>nlog:</c> prefixes are never
    /// declared on it — a strict reader rejects it as an undeclared prefix. Wrapping it in a root
    /// that declares both is what every log4j viewer does, and it keeps the hardened
    /// <see cref="XmlReader.Create(TextReader, XmlReaderSettings)"/> (DTDs prohibited, no resolver)
    /// rather than the legacy namespace-blind reader.
    /// </summary>
    private const string RootOpen =
        """<r xmlns:log4j="http://jakarta.apache.org/log4j/" xmlns:nlog="http://nlog-project.org/dummynamespace/">""";

    private const string RootClose = "</r>";

    /// <summary>
    /// Input is whatever a socket delivered, so nothing here may follow a reference out of the
    /// document: DTDs are prohibited (which alone rules out entity expansion) and there is no
    /// resolver, so no external subset, no schema and no included file can be fetched.
    /// </summary>
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = true,
    };

    /// <summary>Bounds on the epoch milliseconds a <c>timestamp</c> attribute may carry.</summary>
    private static readonly long MinUnixMs = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();
    private static readonly long MaxUnixMs = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    /// <summary>
    /// Intern pool for the low-cardinality values, capped for the same reason as
    /// <see cref="LogParser"/>'s: one parser lives for as long as the app listens, so an uncapped
    /// pool would retain every distinct value ever received.
    /// </summary>
    private readonly Dictionary<string, string> _intern = new(StringComparer.Ordinal);

    private const int MaxInterned = 20_000;

    /// <summary>
    /// Returns null when <paramref name="eventXml"/> is not a parseable log4j event — a truncated
    /// datagram, or something else entirely pointed at the port. Never throws on bad input: the
    /// caller is a receive loop that has to carry on.
    /// </summary>
    /// <param name="eventXml">One <c>&lt;log4j:event&gt;…&lt;/log4j:event&gt;</c> fragment.</param>
    /// <param name="sender">
    /// Where the event came from, used as <see cref="LogEntry.SourceFile"/> when the sender did not
    /// identify itself with a <c>log4japp</c> property.
    /// </param>
    public LogEntry? TryParse(string eventXml, string sender)
    {
        if (string.IsNullOrWhiteSpace(eventXml)) return null;

        string? logger = null, level = null, thread = null, timestamp = null;
        string? environment = null, username = null, company = null, cid = null, app = null, machine = null;
        string? callerClass = null, callerMethod = null, callerFile = null, callerLine = null;
        var message = "";
        var throwable = "";
        var ndc = "";
        var isEvent = false;

        try
        {
            using var reader = XmlReader.Create(
                new StringReader(string.Concat(RootOpen, eventXml, RootClose)), ReaderSettings);

            // ReadElementContentAsString already leaves the reader on the *next* node, so an
            // unconditional Read() at the top of the loop would step straight over the sibling
            // element after a <message> — which is exactly where <throwable> sits.
            var positioned = false;
            while (positioned || reader.Read())
            {
                positioned = false;
                if (reader.NodeType != XmlNodeType.Element) continue;

                // Matched on the local name, so it does not matter which prefix the sender used —
                // properties arrive under log4j: or nlog: depending on includeNLogData.
                switch (reader.LocalName)
                {
                    case "event":
                        isEvent = true;
                        logger = reader.GetAttribute("logger");
                        level = reader.GetAttribute("level");
                        thread = reader.GetAttribute("thread");
                        timestamp = reader.GetAttribute("timestamp");
                        break;

                    case "message":
                        message = reader.ReadElementContentAsString();
                        positioned = true;
                        break;

                    case "throwable":
                        throwable = reader.ReadElementContentAsString();
                        positioned = true;
                        break;

                    case "NDC":
                        ndc = reader.ReadElementContentAsString();
                        positioned = true;
                        break;

                    // log4j's locationInfo carries class/method/file/line (includeCallSite /
                    // includeSourceInfo); NLog's own carries the assembly and nothing else, hence
                    // ??= rather than plain assignment.
                    case "locationInfo":
                        callerClass ??= NullIfEmpty(reader.GetAttribute("class"));
                        callerMethod ??= NullIfEmpty(reader.GetAttribute("method"));
                        callerFile ??= NullIfEmpty(reader.GetAttribute("file"));
                        callerLine ??= NullIfEmpty(reader.GetAttribute("line"));
                        break;

                    case "data":
                        var name = reader.GetAttribute("name");
                        var value = NullIfEmpty(reader.GetAttribute("value"));
                        if (name is null || value is null) break;

                        if (Is(name, "environment") || Is(name, "env")) environment ??= value;
                        else if (Is(name, "username") || Is(name, "user")) username ??= value;
                        else if (Is(name, "company")) company ??= value;
                        else if (Is(name, "cid") || Is(name, "correlationid")) cid ??= value;
                        else if (Is(name, "log4japp")) app ??= value;
                        else if (Is(name, "log4jmachinename")) machine ??= value;
                        break;
                }
            }
        }
        catch (XmlException)
        {
            return null;
        }

        // Well-formed XML that is not a log4j event at all (or an event whose opening tag was lost
        // to truncation) is not an entry.
        if (!isEvent) return null;

        return new LogEntry
        {
            Time = ParseTimestamp(timestamp),
            // Upper-cased because NLog writes its own level names: without this, "Warn" would get
            // its own badge and its own entry in the level filter next to the file logs' "WARN".
            Level = Intern(level?.ToUpperInvariant()) ?? "UNKNOWN",
            ThreadId = Intern(thread) ?? "",
            // No dedicated environment on the wire, so the machine the event came from is the
            // closest thing to one — see the mapping table in the README.
            Environment = Intern(environment ?? machine),
            Username = Intern(username),
            Company = Intern(company),
            // The nested diagnostic context is the sender's own correlation string, so it lands in
            // the correlation column when nothing more specific was sent.
            Cid = Intern(cid ?? NullIfEmpty(ndc)),
            Message = BuildMessage(message, throwable, callerClass, callerMethod, callerFile, callerLine),
            Logger = Intern(logger) ?? "",
            // Which application sent it — the network counterpart of "which file was this line in".
            SourceFile = Intern(app) ?? sender,
        };
    }

    /// <summary>
    /// Folds the event's separate parts into the single-line message shape the rest of the app
    /// expects: real newlines become <see cref="LogEntry.CrLfMarker"/> markers (so the grid keeps
    /// showing one line and the detail dialog still renders the break), and the throwable goes
    /// behind a <see cref="LogEntry.StackTraceMarker"/> so it is recognised as a stack trace.
    /// </summary>
    private static string BuildMessage(
        string message, string throwable,
        string? callerClass, string? callerMethod, string? callerFile, string? callerLine)
    {
        var trace = BuildTrace(throwable, callerClass, callerMethod, callerFile, callerLine);
        var head = Flatten(message.Trim());
        return trace.Length == 0
            ? head
            : string.Concat(head, LogEntry.CrLfMarker, LogEntry.StackTraceMarker, " ", trace);
    }

    /// <summary>
    /// The throwable, preceded by the call site when the sender included one. Returns an empty
    /// string when there is neither, which is the common case for an informational event.
    /// </summary>
    private static string BuildTrace(
        string throwable, string? callerClass, string? callerMethod, string? callerFile, string? callerLine)
    {
        var callSite = FormatCallSite(callerClass, callerMethod, callerFile, callerLine);
        throwable = Flatten(throwable.Trim());

        if (callSite.Length == 0) return throwable;
        return throwable.Length == 0 ? callSite : string.Concat(callSite, LogEntry.CrLfMarker, throwable);
    }

    private static string FormatCallSite(string? cls, string? method, string? file, string? line)
    {
        if (cls is null && method is null && file is null) return "";

        var where = cls is null ? method : method is null ? cls : $"{cls}.{method}";
        if (file is null) return where is null ? "" : $"at {where}";

        var at = line is null ? file : $"{file}:{line}";
        return where is null ? $"at {at}" : $"at {where} in {at}";
    }

    /// <summary>Replaces real line breaks with the marker the file format uses for them.</summary>
    private static string Flatten(string text) =>
        text.Contains('\n') || text.Contains('\r')
            ? text.Replace("\r\n", LogEntry.CrLfMarker).Replace('\r', '\n').Replace("\n", LogEntry.CrLfMarker)
            : text;

    /// <summary>
    /// The <c>timestamp</c> attribute is epoch milliseconds (UTC), shown in local time like every
    /// other timestamp in the app. A sender that writes an ISO string instead is still accepted.
    /// </summary>
    private static DateTime ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DateTime.MinValue;

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
        {
            if (ms < MinUnixMs || ms > MaxUnixMs) return DateTime.MinValue;
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().DateTime;
        }

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : DateTime.MinValue;
    }

    private string? Intern(string? s)
    {
        if (s is null) return null;
        if (_intern.TryGetValue(s, out var existing)) return existing;
        if (_intern.Count < MaxInterned) _intern[s] = s;
        return s;
    }

    private static bool Is(string name, string expected) =>
        string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
