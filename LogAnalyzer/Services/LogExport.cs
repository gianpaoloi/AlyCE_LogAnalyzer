using System.Text;
using System.Text.Json;
using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>Serializes a set of (already filtered) log entries for download.</summary>
public static class LogExport
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss.ffff";

    /// <summary>CSV (UTF-8 with BOM so Excel opens it correctly). One row per entry.</summary>
    public static byte[] ToCsv(IEnumerable<LogEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append('﻿'); // BOM
        sb.AppendLine("time,level,environment,company,username,threadid,cid,logger,message");

        foreach (var e in entries)
        {
            sb.Append(Csv(e.Time.ToString(TimeFormat))).Append(',');
            sb.Append(Csv(e.Level)).Append(',');
            sb.Append(Csv(e.Environment)).Append(',');
            sb.Append(Csv(e.Company)).Append(',');
            sb.Append(Csv(e.Username)).Append(',');
            sb.Append(Csv(e.ThreadId)).Append(',');
            sb.Append(Csv(e.Cid)).Append(',');
            sb.Append(Csv(e.Logger)).Append(',');
            sb.Append(Csv(e.PrettyMessage)).Append("\r\n");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Original JSON-lines format (one JSON object per line), so the subset can be re-loaded.</summary>
    public static byte[] ToJsonLines(IEnumerable<LogEntry> entries)
    {
        using var ms = new MemoryStream();
        foreach (var e in entries)
        {
            using (var w = new Utf8JsonWriter(ms))
            {
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
            }
            ms.WriteByte((byte)'\n');
        }
        return ms.ToArray();
    }

    private static void WriteOptional(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null) w.WriteNull(name);
        else w.WriteString(name, value);
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // Quote if the value contains a delimiter, quote or newline; escape embedded quotes.
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
