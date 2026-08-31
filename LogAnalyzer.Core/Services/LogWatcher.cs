using System.Buffers;
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
    /// Most bytes read in one go. Tailing appends a few KB at a time, but catching up — from the
    /// start of a big file, or after a rotation — would otherwise read the whole remainder into a
    /// single large-object-heap buffer.
    /// </summary>
    private const int MaxChunkBytes = 4 * 1024 * 1024;

    /// <summary>
    /// How much one poll may consume before letting the UI breathe. Chunks within a poll follow
    /// each other immediately: the cap used to end the poll, so catching up on a 1 GB file took one
    /// 750 ms tick per 4 MB — about three minutes of mostly waiting.
    /// </summary>
    private const int MaxChunksPerPoll = 16;

    /// <summary>Ceiling on a partial trailing line carried between polls, as a sanity bound.</summary>
    private const int MaxPendingBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Bytes of the head of the file kept as its fingerprint, to tell "the same file, longer" from
    /// "a different file in the same place". 256 bytes is well into the first log line, which starts
    /// with a timestamp, so two successive files effectively never share a fingerprint.
    /// </summary>
    private const int FingerprintBytes = 256;

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private readonly LogParser _parser = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _position;

    // The partial trailing line, kept as bytes: splitting UTF-8 on '\n' cannot land inside a
    // multi-byte character, which is what made the old char-based chunking fragile.
    private byte[] _pending = [];
    private int _pendingLength;
    private bool _skippingLine;

    /// <summary>Creation time of the file we are following, as a cheap replacement signal.</summary>
    private DateTime? _creationTimeUtc;

    /// <summary>First bytes of the file we are following. See <see cref="FingerprintBytes"/>.</summary>
    private byte[] _head = [];

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

    /// <summary>
    /// Starts following <paramref name="filePath"/>. Awaits the previous poll loop first: it shares
    /// the position and pending-line state, so letting it run on while a new watch reset that state
    /// meant two loops corrupting each other's reads — likely, because a poll on a slow share can
    /// sit in a blocking read for seconds.
    /// </summary>
    public async Task StartAsync(string filePath, bool fromStart)
    {
        await StopAsync().ConfigureAwait(false);

        WatchedFile = filePath;
        LastError = null;
        TotalEntries = 0;
        _pendingLength = 0;
        _skippingLine = false;
        _position = 0;
        _creationTimeUtc = null;
        _head = [];

        try
        {
            // Probed on a worker thread: File.Exists on a wrong or offline UNC path blocks for
            // ~30 s, and the page that called us has to stay usable meanwhile.
            var (creationTimeUtc, length) = await Task.Run(() =>
            {
                var info = new FileInfo(filePath);
                if (!info.Exists)
                    throw new FileNotFoundException($"File not found: {filePath}");

                return (info.CreationTimeUtc, info.Length);
            }).ConfigureAwait(false);

            _creationTimeUtc = creationTimeUtc;
            if (!fromStart) _position = length;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StatusChanged?.Invoke();
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        IsWatching = true;
        StatusChanged?.Invoke();
        _loop = Task.Run(() => PollLoopAsync(filePath, cts.Token), CancellationToken.None);
    }

    /// <summary>Stops following the file and waits for the poll loop to actually finish.</summary>
    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;

        if (cts is null)
        {
            if (IsWatching)
            {
                IsWatching = false;
                StatusChanged?.Invoke();
            }

            return;
        }

        try { cts.Cancel(); } catch (ObjectDisposedException) { /* already gone */ }

        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); } catch { /* cancellation, or an error already reported */ }
        }

        cts.Dispose();
        ReleasePending();
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
                    ReadNew(filePath, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    StatusChanged?.Invoke();
                }
            } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
        catch (ObjectDisposedException)
        {
            // The timer or the token source went away underneath us while stopping.
        }
    }

    private void ReadNew(string filePath, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) return;

        // Shrinking is visible without opening anything, and an idle file needs no I/O at all —
        // rotation to a file of exactly the same length is picked up on its next append.
        if (info.Length < _position) RestartFrom(info);
        if (info.Length == _position && !CreationTimeChanged(info)) return;

        // One handle for the whole poll: catching up used to reopen the file per chunk, which on a
        // remote share costs a round trip each time.
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (CreationTimeChanged(info) || HeadChanged(fs, info.Length)) RestartFrom(info);
        CaptureHead(fs, info.Length);

        for (var chunk = 0; chunk < MaxChunksPerPoll; chunk++)
        {
            ct.ThrowIfCancellationRequested();
            if (info.Length <= _position) return;
            if (!ReadChunk(fs, info.Length, Path.GetFileName(filePath), ct)) return;

            // The writer has probably appended more while we were reading.
            info.Refresh();
        }
    }

    /// <summary>
    /// Cheap replacement signal. Not sufficient on its own: NTFS "file tunneling" hands a file
    /// recreated within ~15 s of its predecessor being renamed away that predecessor's creation
    /// time — which is exactly what log rotation does — so <see cref="HeadChanged"/> is what
    /// actually catches the common case.
    /// </summary>
    private bool CreationTimeChanged(FileInfo info) =>
        _creationTimeUtc is { } known && info.CreationTimeUtc != known;

    /// <summary>
    /// True when the start of the file no longer matches what we recorded, i.e. this is a different
    /// file in the same place. This is the check that catches the rotation that renames the file
    /// away and creates a new, longer one: the length never drops below the read position, so the
    /// tail used to go quiet for good.
    /// </summary>
    private bool HeadChanged(FileStream fs, long length)
    {
        if (_head.Length == 0) return false;          // nothing recorded yet
        if (length < _head.Length) return true;       // shorter than its own beginning

        Span<byte> current = stackalloc byte[FingerprintBytes];
        current = current[.._head.Length];

        fs.Seek(0, SeekOrigin.Begin);
        var read = fs.ReadAtLeast(current, current.Length, throwOnEndOfStream: false);
        return read != _head.Length || !current.SequenceEqual(_head);
    }

    /// <summary>
    /// Records the head of the file, extending it as the file grows until it reaches
    /// <see cref="FingerprintBytes"/> — a log that is still only a few bytes long has nothing
    /// distinctive to record yet.
    /// </summary>
    private void CaptureHead(FileStream fs, long length)
    {
        if (_head.Length >= FingerprintBytes || length <= _head.Length) return;

        var size = (int)Math.Min(length, FingerprintBytes);
        var head = new byte[size];

        fs.Seek(0, SeekOrigin.Begin);
        var read = fs.ReadAtLeast(head, size, throwOnEndOfStream: false);
        if (read > 0) _head = read == size ? head : head[..read];
    }

    private void RestartFrom(FileInfo info)
    {
        _position = 0;
        _pendingLength = 0;
        _skippingLine = false;
        _creationTimeUtc = info.CreationTimeUtc;
        _head = [];
    }

    /// <summary>Reads one bounded chunk. Returns false when there was nothing to read.</summary>
    private bool ReadChunk(FileStream fs, long length, string fileName, CancellationToken ct)
    {
        var toRead = (int)Math.Min(length - _position, MaxChunkBytes);
        if (toRead <= 0) return false;

        // Pooled: a 4 MB array per poll went straight onto the large-object heap.
        var buffer = ArrayPool<byte>.Shared.Rent(toRead);
        try
        {
            fs.Seek(_position, SeekOrigin.Begin);
            var read = fs.ReadAtLeast(buffer.AsSpan(0, toRead), toRead, throwOnEndOfStream: false);
            if (read <= 0) return false;

            var startedAtZero = _position == 0;
            _position += read;

            var span = buffer.AsSpan(0, read);
            // A StreamReader would have eaten the BOM; the parser must not see it on the first line.
            if (startedAtZero && span.StartsWith(Utf8Bom)) span = span[Utf8Bom.Length..];

            var entries = SplitAndParse(span, fileName, ct);
            if (entries.Count > 0)
            {
                TotalEntries += entries.Count;
                LastActivity = DateTime.Now;
                EntriesAppended?.Invoke(entries);
                StatusChanged?.Invoke();
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Splits a chunk on newlines and parses each complete line. Whatever follows the last newline
    /// is carried over to the next chunk.
    /// </summary>
    private List<LogEntry> SplitAndParse(ReadOnlySpan<byte> span, string fileName, CancellationToken ct)
    {
        var entries = new List<LogEntry>();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var newline = span.IndexOf((byte)'\n');
            if (newline < 0) break;

            var line = TrimCarriageReturn(span[..newline]);
            span = span[(newline + 1)..];

            if (_skippingLine)
            {
                // Tail of a line that outgrew the pending buffer; the next one starts clean.
                _skippingLine = false;
                _pendingLength = 0;
                continue;
            }

            if (_pendingLength > 0)
            {
                AppendPending(line);
                var joined = TrimCarriageReturn(_pending.AsSpan(0, _pendingLength));
                Parse(entries, joined, fileName);
                _pendingLength = 0;
            }
            else
            {
                Parse(entries, line, fileName);
            }
        }

        if (!span.IsEmpty && !_skippingLine) AppendPending(span);
        return entries;
    }

    private void Parse(List<LogEntry> entries, ReadOnlySpan<byte> line, string fileName)
    {
        var entry = _parser.TryParse(line, fileName);
        if (entry is not null) entries.Add(entry);
    }

    private void AppendPending(ReadOnlySpan<byte> tail)
    {
        var needed = _pendingLength + tail.Length;
        if (needed > MaxPendingBytes)
        {
            // Not a log line by any reasonable definition; drop it and resync at the next newline.
            _skippingLine = true;
            _pendingLength = 0;
            return;
        }

        if (_pending.Length < needed)
        {
            var bigger = ArrayPool<byte>.Shared.Rent(Math.Max(needed, 8 * 1024));
            if (_pendingLength > 0) Buffer.BlockCopy(_pending, 0, bigger, 0, _pendingLength);
            ReturnPendingBuffer();
            _pending = bigger;
        }

        tail.CopyTo(_pending.AsSpan(_pendingLength));
        _pendingLength += tail.Length;
    }

    private void ReleasePending()
    {
        ReturnPendingBuffer();
        _pending = [];
        _pendingLength = 0;
    }

    private void ReturnPendingBuffer()
    {
        if (_pending.Length > 0) ArrayPool<byte>.Shared.Return(_pending);
    }

    private static ReadOnlySpan<byte> TrimCarriageReturn(ReadOnlySpan<byte> line) =>
        line.Length > 0 && line[^1] == (byte)'\r' ? line[..^1] : line;

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
