using System.Text.RegularExpressions;

namespace LogAnalyzer.Services;

/// <summary>
/// Turns a concrete log message into a stable "signature" by masking the parts that vary
/// between otherwise-identical events (guids, numbers, durations, quoted values, tenant ids).
/// Used to cluster similar WARN/ERROR entries together in the triage view.
/// </summary>
public static partial class MessageNormalizer
{
    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"\b\d{2,4}[-/]\d{1,2}[-/]\d{1,4}\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\b\d{1,2}:\d{2}:\d{2}(\.\d+)?\b")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"\b[0-9A-Fa-f]{16,}\b")]
    private static partial Regex HexRegex();

    [GeneratedRegex(@"'[^']*'")]
    private static partial Regex QuotedRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static string Signature(string message)
    {
        if (string.IsNullOrEmpty(message)) return "";

        // Drop the stack trace tail: keep only the human part of the message.
        var idx = message.IndexOf("stackTrace:", StringComparison.OrdinalIgnoreCase);
        var s = idx >= 0 ? message[..idx] : message;

        var crlf = s.IndexOf("\\CRLF", StringComparison.Ordinal);
        if (crlf >= 0) s = s[..crlf];

        s = GuidRegex().Replace(s, "{GUID}");
        s = DurationRegex().Replace(s, "{DURATION}");
        s = DateRegex().Replace(s, "{DATE}");
        s = HexRegex().Replace(s, "{HEX}");
        s = QuotedRegex().Replace(s, "'{VAL}'");
        s = NumberRegex().Replace(s, "{N}");
        s = WhitespaceRegex().Replace(s, " ").Trim();

        return s.Length > 400 ? s[..400] : s;
    }
}
