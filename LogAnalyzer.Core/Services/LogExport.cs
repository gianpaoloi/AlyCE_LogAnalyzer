using System.Buffers;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>
/// Serializes a set of (already filtered) log entries for download.
/// <para>
/// Everything writes straight into a stream. Building the whole export as a string first meant a
/// StringBuilder, a string and a byte array all holding the same data at once — around four copies
/// of a set that can be hundreds of megabytes, which is an easy way to run out of memory on a
/// download the user thinks is free.
/// </para>
/// </summary>
public static class LogExport
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss.ffff";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Static, because this was allocated once per field — nine times per exported row.</summary>
    private static readonly SearchValues<char> MustQuote = SearchValues.Create(",\"\n\r");

    /// <summary>
    /// Leading characters Excel and LibreOffice treat as the start of a formula. Log messages are
    /// attacker-influenced text, so they get a <c>'</c> in front. <c>-</c> is deliberately not in
    /// the list: plenty of log lines legitimately start with one and spreadsheets read those as
    /// negative numbers, so escaping them would mangle far more than it protects.
    /// </summary>
    private static readonly SearchValues<char> FormulaStart = SearchValues.Create("=+@\t\r");

    private static readonly JsonWriterOptions JsonOptions = new()
    {
        // JSON Lines is a sequence of root documents, which the writer would otherwise reject.
        SkipValidation = true,
    };

    /// <summary>
    /// Writes the entries to a temp file and hands back a stream over it, ready to be passed to
    /// the browser. The file deletes itself when the stream is closed.
    /// </summary>
    public static async Task<Stream> OpenExportAsync(
        IEnumerable<LogEntry> entries, bool jsonLines, CancellationToken ct = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"alyce-export-{Guid.NewGuid():N}.tmp");

        await using (var write = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            if (jsonLines) WriteJsonLines(write, entries, ct);
            else WriteCsv(write, entries, ct);
        }

        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None,
                              bufferSize: 64 * 1024, FileOptions.DeleteOnClose);
    }

    /// <summary>CSV (UTF-8 with BOM so Excel opens it correctly). One row per entry.</summary>
    public static void WriteCsv(Stream output, IEnumerable<LogEntry> entries, CancellationToken ct = default)
    {
        using var writer = new StreamWriter(output, Utf8NoBom, bufferSize: 64 * 1024, leaveOpen: true)
        {
            NewLine = "\r\n",
        };

        writer.Write('﻿'); // BOM
        writer.WriteLine("time,level,environment,company,username,threadid,cid,logger,message");

        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();

            WriteField(writer, e.Time.ToString(TimeFormat)); writer.Write(',');
            WriteField(writer, e.Level); writer.Write(',');
            WriteField(writer, e.Environment); writer.Write(',');
            WriteField(writer, e.Company); writer.Write(',');
            WriteField(writer, e.Username); writer.Write(',');
            WriteField(writer, e.ThreadId); writer.Write(',');
            WriteField(writer, e.Cid); writer.Write(',');
            WriteField(writer, e.Logger); writer.Write(',');
            WriteField(writer, e.PrettyMessage);
            writer.WriteLine();
        }
    }

    /// <summary>Original JSON-lines format (one JSON object per line), so the subset can be re-loaded.</summary>
    public static void WriteJsonLines(Stream output, IEnumerable<LogEntry> entries, CancellationToken ct = default)
    {
        // One writer for the whole export, reset per line: constructing one per entry allocated a
        // fresh output buffer for every row.
        using var w = new Utf8JsonWriter(output, JsonOptions);

        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();

            w.WriteStartObject();
            w.WriteString("time", e.Time.ToString(TimeFormat));
            w.WriteString("level", e.Level);
            w.WriteString("threadid", e.ThreadId);
            WriteOptional(w, "environment", e.Environment);
            WriteOptional(w, "username", e.Username);
            WriteOptional(w, "company", e.Company);
            WriteOptional(w, "cid", e.Cid);
            w.WriteString("message", e.Message);
            w.WriteString("logger", e.Logger);
            w.WriteEndObject();
            w.Flush();

            output.WriteByte((byte)'\n');
            w.Reset();
        }
    }

    /// <summary>In-memory variants, for callers that only need a handful of rows (and for tests).</summary>
    public static byte[] ToCsv(IEnumerable<LogEntry> entries)
    {
        using var ms = new MemoryStream();
        WriteCsv(ms, entries);
        return ms.ToArray();
    }

    public static byte[] ToJsonLines(IEnumerable<LogEntry> entries)
    {
        using var ms = new MemoryStream();
        WriteJsonLines(ms, entries);
        return ms.ToArray();
    }

    private static void WriteOptional(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null) w.WriteNull(name);
        else w.WriteString(name, value);
    }

    private static void WriteField(TextWriter writer, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;

        var escapeFormula = FormulaStart.Contains(value[0]);
        if (!escapeFormula && !value.AsSpan().ContainsAny(MustQuote))
        {
            writer.Write(value);
            return;
        }

        writer.Write('"');
        if (escapeFormula) writer.Write('\'');

        // Escape embedded quotes by doubling them, writing the runs between as-is.
        var rest = value.AsSpan();
        while (true)
        {
            var quote = rest.IndexOf('"');
            if (quote < 0) break;
            writer.Write(rest[..quote]);
            writer.Write("\"\"");
            rest = rest[(quote + 1)..];
        }

        writer.Write(rest);
        writer.Write('"');
    }
}
