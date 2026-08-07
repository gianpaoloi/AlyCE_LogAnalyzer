using System.Text;
using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>
/// Tails a single log file (local or on a remote/UNC share) by polling for appended content.
/// Polling is used rather than FileSystemWatcher because it works reliably across network shares
/// and while another process holds the file open for writing. New parsed entries are pushed to
/// subscribers so the live view can update in real time.
/// </summary>
public sealed class LogWatcher : IAsyncDisposable
{
    /// <summary>
    /// Most bytes decoded in one poll. Tailing appends a few KB at a time, but catching up — from the
    /// start of a big file, or after a rotation — used to decode the whole remainder into a single
    /// string, which lands on the large-object heap and stalls the app. Whatever is left follows on the
    /// next tick.
    /// </summary>
    private const int MaxChunkBytes = 4 * 1024 * 1024;

    private readonly LogParser _parser = new();
    // Kept across polls: a multi-byte character split over two chunks would otherwise decode to
    // garbage on both sides of the boundary.
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _position;
    private string _pending = "";      // carries an incomplete trailing line between polls

    public bool IsWatching { get; private set; }
    public string? WatchedFile { get; private set; }
    public long TotalEntries { get; private set; }
    public DateTime? LastActivity { get; private set; }
    public string? LastError { get; private set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(750);

    /// <summary>Raised (on a background thread) whenever one or more new entries are read.</summary>
    public event Action<IReadOnlyList<LogEntry>>? EntriesAppended;
    /// <summary>Raised when watch status/error changes.</summary>
    public event Action? StatusChanged;

    public void Start(string filePath, bool fromStart)
    {
        Stop();

        WatchedFile = filePath;
        LastError = null;
        TotalEntries = 0;
        _pending = "";
        _position = 0;

        try
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            if (!fromStart)
                _position = new FileInfo(filePath).Length;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StatusChanged?.Invoke();
            return;
        }

        _cts = new CancellationTokenSource();
        IsWatching = true;
        StatusChanged?.Invoke();
        _loop = Task.Run(() => PollLoopAsync(filePath, _cts.Token));
    }

    public void Stop()
    {
        if (_cts is null) return;
        try { _cts.Cancel(); } catch { /* ignore */ }
        _cts.Dispose();
        _cts = null;
        _loop = null;
        IsWatching = false;
        StatusChanged?.Invoke();
    }

    private async Task PollLoopAsync(string filePath, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            do
            {
                try
                {
                    ReadNew(filePath);
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    StatusChanged?.Invoke();
                }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
    }

    private void ReadNew(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) return;

        // File shrank -> assume rotation/truncation; start over.
        if (info.Length < _position)
        {
            _position = 0;
            _pending = "";
            _decoder.Reset();
        }
        if (info.Length == _position) return;

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(_position, SeekOrigin.Begin);

        var toRead = (int)Math.Min(info.Length - _position, MaxChunkBytes);
        var bytes = new byte[toRead];
        var read = fs.ReadAtLeast(bytes, toRead, throwOnEndOfStream: false);
        if (read <= 0) return;
        var startedAtZero = _position == 0;
        _position += read;

        // Decoded by hand rather than with a StreamReader: the reader buffers ahead, so stopping at the
        // cap would leave _position past the text actually decoded and silently drop lines.
        var chars = new char[_decoder.GetCharCount(bytes, 0, read)];
        var decoded = _decoder.GetChars(bytes, 0, read, chars, 0);
        var chunk = new string(chars, 0, decoded);
        // A StreamReader would have eaten the BOM; the parser must not see it on the first line.
        if (startedAtZero && chunk.StartsWith('﻿')) chunk = chunk[1..];

        var text = _pending + chunk;
        var lines = text.Split('\n');
        // Last element is an incomplete line unless the chunk ended with '\n'.
        _pending = lines[^1];

        var newEntries = new List<LogEntry>();
        for (var i = 0; i < lines.Length - 1; i++)
        {
            var entry = _parser.TryParse(lines[i].AsSpan().TrimEnd('\r'), Path.GetFileName(filePath));
            if (entry is not null) newEntries.Add(entry);
        }

        if (newEntries.Count > 0)
        {
            TotalEntries += newEntries.Count;
            LastActivity = DateTime.Now;
            EntriesAppended?.Invoke(newEntries);
            StatusChanged?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_loop is not null)
        {
            try { await _loop; } catch { /* ignore */ }
        }
    }
}
