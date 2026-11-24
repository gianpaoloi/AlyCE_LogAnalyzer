using System.Text.RegularExpressions;

namespace LogAnalyzer.Services.Ai;

/// <summary>
/// Masks the values that most often leak out of logs before the text is sent to a provider: e-mail
/// addresses, IP addresses, bearer tokens and "password=..." style pairs. It is a best effort, not a
/// guarantee, which is why the user reads the exact text in the preview before anything is sent.
/// </summary>
public static class AiRedactor
{
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        (new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}"), "[email]"),
        (new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b"), "[ip]"),
        (new Regex(@"(?i)\bbearer\s+[A-Za-z0-9\-._~+/]+=*"), "Bearer [redacted]"),
        // Keeps the name and separator ("password=", "\"token\": \"") and masks only the value.
        (new Regex(@"(?i)(""?(?:password|passwd|pwd|secret|token|api[_-]?key|apikey|authorization|connection[_-]?string)""?\s*[:=]\s*""?)[^\s"",;&}]+"),
            "$1[redacted]"),
    ];

    /// <summary>The masked text and how many values were masked.</summary>
    public static (string Text, int Replacements) Redact(string text)
    {
        var count = 0;
        var result = text;
        foreach (var (pattern, replacement) in Rules)
        {
            result = pattern.Replace(result, m =>
            {
                count++;
                return m.Result(replacement);
            });
        }

        return (result, count);
    }
}
