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
    private readonly LogParser _parser = new();
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
        }
        if (info.Length == _position) return;

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(_position, SeekOrigin.Begin);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        var chunk = reader.ReadToEnd();
        _position = fs.Position;

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
