using System.Text;
using System.Text.Json;

namespace LogAnalyzer.Services;

/// <summary>
/// Reads a log export downloaded from Grafana. The export is a JSON array of records shaped like
/// <c>{ "line": "&lt;webapi JSON record&gt;", "timestamp": "...", "date": "...", "fields": { ... } }</c>.
/// The <c>line</c> value is already a webapi log record, so this converts the array into JSON lines
/// that <see cref="LogParser"/> reads like any <c>.log</c> file. Records are streamed one at a time,
/// so a large export is never held in memory whole.
/// </summary>
public static class GrafanaExport
{
    /// <summary>Outcome of a conversion: how many records became lines, and how many were not usable.</summary>
    public readonly record struct Result(int Written, int Skipped);

    /// <summary>True when the file name marks a candidate Grafana export (<c>.json</c>).</summary>
    public static bool IsExportName(string name) =>
        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes each record's <c>line</c> to <paramref name="output"/> as one UTF-8 line (no BOM, LF endings).
    /// Records without a usable <c>line</c> are counted as skipped, never guessed at.
    /// </summary>
    /// <exception cref="InvalidDataException">The input is not a JSON array of records.</exception>
    public static async Task<Result> ConvertAsync(Stream json, Stream output, CancellationToken ct)
    {
        var written = 0;
        var skipped = 0;

        await using var writer = new StreamWriter(output, new UTF8Encoding(false), bufferSize: 64 * 1024, leaveOpen: true)
        {
            NewLine = "\n",
        };

        try
        {
            await foreach (var element in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(json, cancellationToken: ct))
            {
                if (element.ValueKind == JsonValueKind.Object &&
                    element.TryGetProperty("line", out var lineValue) &&
                    lineValue.ValueKind == JsonValueKind.String &&
                    lineValue.GetString() is { Length: > 0 } text)
                {
                    // A record is one line: a raw line break inside the value would split it in two.
                    await writer.WriteLineAsync(text.Replace('\r', ' ').Replace('\n', ' ').AsMemory(), ct);
                    written++;
                }
                else
                {
                    skipped++;
                }
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "This JSON file is not a Grafana log export (expected a JSON array of log records).", ex);
        }

        await writer.FlushAsync(ct);
        return new Result(written, skipped);
    }
}
