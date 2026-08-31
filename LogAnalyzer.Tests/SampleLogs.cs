namespace LogAnalyzer.Tests;

/// <summary>Access to the real sample logs copied next to the test assembly.</summary>
internal static class SampleLogs
{
    public static IEnumerable<string> Files()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Example-Logs");
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.log").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            : Array.Empty<string>();
    }

    /// <summary>Every message field in the sample corpus — the corpus the signature tests use.</summary>
    public static IEnumerable<string> Messages()
    {
        var parser = new Services.LogParser();
        foreach (var file in Files())
        {
            using var stream = File.OpenRead(file);
            using var reader = new Services.Utf8LineReader(stream);
            while (reader.TryReadLine(out var line))
            {
                var entry = parser.TryParse(line, "sample.log");
                if (entry is not null && entry.Message.Length > 0) yield return entry.Message;
            }
        }
    }
}
