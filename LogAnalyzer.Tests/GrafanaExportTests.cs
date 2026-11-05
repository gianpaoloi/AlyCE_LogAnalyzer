using System.Text;
using System.Text.Json;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class GrafanaExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"alyce-grafana-tests-{Guid.NewGuid():N}");

    public GrafanaExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A webapi record, serialized the way the app writes it.</summary>
    private static string Record(string time, string level, string message) =>
        JsonSerializer.Serialize(new { time, level, threadid = ".NET TP Worker", environment = "E1",
                                       company = "1", logger = "A.B", message });

    /// <summary>One element of a Grafana export: the record wrapped as a JSON string in "line".</summary>
    private static string Element(string line) =>
        "{" + $"\"line\":{JsonSerializer.Serialize(line)},\"timestamp\":\"1793873075056751100\"," +
        "\"date\":\"2026-07-08T10:00:00.000Z\",\"fields\":{\"hostname\":\"srv\",\"message\":\"x\"}" + "}";

    private static string Export(params string[] elements) => "[" + string.Join(",", elements) + "]";

    private static async Task<(GrafanaExport.Result Result, string Text)> ConvertText(string json)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        using var output = new MemoryStream();
        var result = await GrafanaExport.ConvertAsync(input, output, CancellationToken.None);
        return (result, Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void Recognises_json_export_names_case_insensitively()
    {
        Assert.True(GrafanaExport.IsExportName("Logs-2026-11-05 11_24_04.json"));
        Assert.True(GrafanaExport.IsExportName("LOGS.JSON"));
        Assert.False(GrafanaExport.IsExportName("all_2026-07-08.log"));
    }

    [Fact]
    public async Task Writes_each_record_as_one_line_in_file_order()
    {
        var json = Export(
            Element(Record("2026-07-08 10:00:02.0000", "ERROR", "second")),
            Element(Record("2026-07-08 10:00:01.0000", "ERROR", "first")));

        var (result, text) = await ConvertText(json);

        Assert.Equal(2, result.Written);
        Assert.Equal(0, result.Skipped);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal(Record("2026-07-08 10:00:02.0000", "ERROR", "second"), lines[0]);
        Assert.Equal(Record("2026-07-08 10:00:01.0000", "ERROR", "first"), lines[1]);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public async Task Keeps_a_pretty_printed_record_on_one_line()
    {
        // A "line" value holding real line breaks between JSON tokens must still become one record.
        var pretty = JsonSerializer.Serialize(
            new { time = "2026-07-08 10:00:00.0000", level = "ERROR", message = "boom" },
            new JsonSerializerOptions { WriteIndented = true });
        Assert.Contains('\n', pretty);
        var json = Export(Element(pretty));

        var (result, text) = await ConvertText(json);

        Assert.Equal(1, result.Written);
        var line = text.TrimEnd('\n');
        Assert.DoesNotContain('\n', line);
        Assert.Equal("boom", JsonDocument.Parse(line).RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Counts_elements_without_a_usable_line_as_skipped()
    {
        var json = Export(
            Element(Record("2026-07-08 10:00:00.0000", "ERROR", "kept")),
            "{\"timestamp\":\"1\",\"fields\":{}}",
            "{\"line\":\"\"}",
            "{\"line\":42}",
            "\"not an object\"");

        var (result, text) = await ConvertText(json);

        Assert.Equal(1, result.Written);
        Assert.Equal(4, result.Skipped);
        Assert.Single(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Rejects_json_that_is_not_an_array_of_records()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => ConvertText("{\"level\":\"ERROR\"}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => ConvertText("not json at all"));
    }

    [Fact]
    public async Task Loads_a_grafana_export_from_disk_like_a_log_file()
    {
        var path = Path.Combine(_dir, "Logs-2026-07-08 10_00_00.json");
        File.WriteAllText(path, Export(
            Element(Record("2026-07-08 10:00:02.0000", "ERROR", "second")),
            Element(Record("2026-07-08 10:00:01.0000", "ERROR", "first"))), new UTF8Encoding(false));

        var store = new LogStore();
        await store.LoadFromPathsAsync(new[] { path }, includeDebug: false, CancellationToken.None);

        Assert.Null(store.LoadError);
        Assert.Equal(2, store.Entries.Count);
        Assert.Equal("first", store.Entries[0].Message);          // merged into time order
        Assert.Equal("Logs-2026-07-08 10_00_00.json", store.Entries[0].SourceFile);
    }

    [Fact]
    public async Task Reports_a_json_file_that_is_not_an_export()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{\"theme\":\"dark\"}", new UTF8Encoding(false));

        var store = new LogStore();
        await store.LoadFromPathsAsync(new[] { path }, includeDebug: false, CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(store.LoadError));
        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task Uploaded_grafana_export_is_appended_like_a_log_upload()
    {
        var bytes = Encoding.UTF8.GetBytes(Export(
            Element(Record("2026-07-08 10:00:00.0000", "ERROR", "uploaded"))));

        var store = new LogStore();
        await store.EnqueueFromStreamsAsync(
            new (string, Func<Stream>)[] { ("export.json", () => new MemoryStream(bytes)) },
            includeDebug: false, CancellationToken.None);

        Assert.Null(store.LoadError);
        Assert.Single(store.Entries);
        Assert.Equal("uploaded", store.Entries[0].Message);
        Assert.Equal("export.json", store.Entries[0].SourceFile);
    }
}
