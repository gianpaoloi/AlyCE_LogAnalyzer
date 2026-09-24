using System.Net;
using System.Net.Sockets;
using LogAnalyzer.Models;

namespace LogAnalyzer.Services;

/// <summary>
/// Receives log events pushed over the network by NLog's <c>NLogViewer</c> target and publishes them
/// as <see cref="LogEntry"/> instances, so a running application can be watched without it writing a
/// file at all. The file counterpart is <see cref="LogWatcher"/>, and the two deliberately expose the
/// same surface (status, counters, <see cref="EntriesAppended"/>) because the two pages that consume
/// them are the same page bar the settings card.
/// <para>
/// The sender side is one target in the application's <c>NLog.config</c>:
/// </para>
/// <code>
/// &lt;target name="viewer" xsi:type="NLogViewer" address="udp://127.0.0.1:9999" /&gt;
/// </code>
/// <para>
/// UDP only, which is what that target defaults to and what makes it safe to point a production
/// process at a listener that may not be running: nothing blocks, nothing retries, and a receiver
/// that cannot keep up loses datagrams instead of slowing the sender down.
/// </para>
/// </summary>
public sealed class NetworkLogListener : IAsyncDisposable
{
    /// <summary>Port used by the sample configuration in the docs and prefilled in the UI.</summary>
    public const int DefaultPort = 9999;

    /// <summary>
    /// Kernel receive buffer. The default (8 KB on Windows) holds barely a handful of events, and a
    /// burst that overruns it is dropped by the OS before this process ever sees it.
    /// </summary>
    private const int SocketReceiveBufferBytes = 4 * 1024 * 1024;

    /// <summary>
    /// How often received events are handed to subscribers. One event per datagram would raise
    /// <see cref="EntriesAppended"/> thousands of times a second under load, and each of those costs
    /// the consumer a lock and a set of published-list rebuilds; batching keeps that flat.
    /// </summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Ceiling on the batch waiting to be flushed. Reached only if events arrive faster than the
    /// 200 ms flush can publish them, in which case the newest are what matter — the consumer keeps
    /// the newest 1 000 anyway.
    /// </summary>
    private const int MaxPendingEntries = 100_000;

    /// <summary>
    /// One splitter per sender, because a payload split across datagrams must be reassembled per
    /// sender and two applications logging to the same port interleave their sends.
    /// </summary>
    private readonly Dictionary<string, Log4JEventSplitter> _splitters = new(StringComparer.Ordinal);

    /// <summary>
    /// Cap on those, so a spoofed or scanning source cannot make this dictionary the leak. Well
    /// past any real deployment: a sending process keeps one socket, so it is one entry.
    /// </summary>
    private const int MaxSenders = 256;

    private readonly Log4JXmlParser _parser = new();

    private readonly object _gate = new();
    private List<LogEntry> _pending = new();

    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _flushLoop;
    private UdpClient? _socket;

    public bool IsListening { get; private set; }

    /// <summary>What the UI shows and what a sender's <c>address</c> has to match, e.g. <c>udp://127.0.0.1:9999</c>.</summary>
    public string? Endpoint { get; private set; }

    /// <summary>Events parsed and published since this listener was last started.</summary>
    public long TotalEntries { get; private set; }

    /// <summary>Datagrams received, whatever they turned out to contain.</summary>
    public long TotalDatagrams { get; private set; }

    /// <summary>
    /// Datagrams that yielded no event — something other than an NLogViewer target is pointed at the
    /// port, or a payload lost the piece carrying its opening tag. Surfaced in the UI because
    /// "packets are arriving but the grid is empty" is otherwise impossible to tell from silence.
    /// </summary>
    public long Ignored { get; private set; }

    public DateTime? LastActivity { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Raised (on a background thread) with each batch of newly received events.</summary>
    public event Action<IReadOnlyList<LogEntry>>? EntriesAppended;

    /// <summary>Raised when the listening status, an error or a counter changes.</summary>
    public event Action? StatusChanged;

    /// <summary>
    /// Binds the port and starts receiving. Any previous listener is stopped and awaited first — it
    /// owns the socket and the per-sender reassembly state, so two loops must never overlap.
    /// </summary>
    /// <param name="port">UDP port to bind.</param>
    /// <param name="allInterfaces">
    /// False binds the loopback address only, which is what <c>udp://127.0.0.1:9999</c> needs and
    /// keeps the port unreachable from the network. True binds every interface, for events sent from
    /// another machine — and opens a port on this one, so it is off by default.
    /// </param>
    public async Task StartAsync(int port, bool allInterfaces)
    {
        await StopAsync().ConfigureAwait(false);

        LastError = null;
        TotalEntries = 0;
        TotalDatagrams = 0;
        Ignored = 0;
        var address = allInterfaces ? IPAddress.Any : IPAddress.Loopback;
        Endpoint = $"udp://{(allInterfaces ? "0.0.0.0" : "127.0.0.1")}:{port}";

        if (port is < 1 or > 65535)
        {
            LastError = $"Port {port} is out of range (1-65535).";
            StatusChanged?.Invoke();
            return;
        }

        UdpClient socket;
        try
        {
            socket = new UdpClient(new IPEndPoint(address, port));
            socket.Client.ReceiveBufferSize = SocketReceiveBufferBytes;
            DisableConnectionReset(socket);
        }
        catch (SocketException ex)
        {
            // Overwhelmingly: the port is already taken (by another copy of this app, or by the
            // real NLog viewer). Reported as-is rather than swallowed, because the alternative is a
            // page that says it is listening and never shows a line.
            LastError = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"Port {port} is already in use by another application."
                : ex.Message;
            StatusChanged?.Invoke();
            return;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StatusChanged?.Invoke();
            return;
        }

        _socket = socket;
        var cts = new CancellationTokenSource();
        _cts = cts;
        IsListening = true;
        StatusChanged?.Invoke();

        _receiveLoop = Task.Run(() => ReceiveLoopAsync(socket, cts.Token), CancellationToken.None);
        _flushLoop = Task.Run(() => FlushLoopAsync(cts.Token), CancellationToken.None);
    }

    /// <summary>Closes the socket and waits for both loops to finish.</summary>
    public async Task StopAsync()
    {
        var cts = _cts;
        var receive = _receiveLoop;
        var flush = _flushLoop;
        var socket = _socket;
        _cts = null;
        _receiveLoop = null;
        _flushLoop = null;
        _socket = null;

        if (cts is null)
        {
            if (IsListening)
            {
                IsListening = false;
                StatusChanged?.Invoke();
            }

            return;
        }

        try { cts.Cancel(); } catch (ObjectDisposedException) { /* already gone */ }

        // Closed before awaiting: a socket parked in ReceiveAsync does not necessarily observe the
        // token until a datagram arrives, and on an idle port that is never.
        socket?.Dispose();

        foreach (var loop in new[] { receive, flush })
        {
            if (loop is null) continue;
            try { await loop.ConfigureAwait(false); } catch { /* cancellation, or an error already reported */ }
        }

        cts.Dispose();
        _splitters.Clear();

        // Whatever the flush loop had not published yet is still worth showing: the events were
        // received, and stopping a capture is exactly when the last few matter most.
        Flush();

        IsListening = false;
        StatusChanged?.Invoke();
    }

    private async Task ReceiveLoopAsync(UdpClient socket, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try
                {
                    received = await socket.ReceiveAsync(ct).ConfigureAwait(false);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // Windows reports an ICMP port-unreachable from an earlier send as an error on
                    // the *receiving* socket. It says nothing about this socket's health, so it must
                    // not end the loop. See DisableConnectionReset, which suppresses most of them.
                    continue;
                }
                catch (SocketException ex)
                {
                    LastError = ex.Message;
                    StatusChanged?.Invoke();
                    continue;
                }

                TotalDatagrams++;
                Consume(received.Buffer, Describe(received.RemoteEndPoint));
            }
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
        catch (ObjectDisposedException)
        {
            // The socket was closed underneath us by StopAsync; that is how an idle receive ends.
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StatusChanged?.Invoke();
        }
    }

    /// <summary>Reassembles, parses and queues everything one datagram turned out to contain.</summary>
    private void Consume(byte[] payload, string sender)
    {
        if (!_splitters.TryGetValue(sender, out var splitter))
        {
            // At the cap the oldest reassembly state goes: keeping a partial event for a sender
            // that has gone quiet matters far less than still accepting the ones that are talking.
            if (_splitters.Count >= MaxSenders) _splitters.Clear();
            splitter = new Log4JEventSplitter();
            _splitters[sender] = splitter;
        }

        splitter.Append(payload);

        var parsed = 0;
        while (splitter.TryTake(out var eventXml))
        {
            var entry = _parser.TryParse(eventXml, sender);
            if (entry is null) continue;

            parsed++;
            lock (_gate)
            {
                if (_pending.Count < MaxPendingEntries) _pending.Add(entry);
            }
        }

        if (parsed > 0)
        {
            TotalEntries += parsed;
            LastActivity = DateTime.Now;
        }
        else if (!splitter.HasPartial)
        {
            // Nothing came out of it and nothing is still being assembled, so this datagram was not
            // an NLogViewer payload.
            Ignored++;
            StatusChanged?.Invoke();
        }
    }

    /// <summary>Publishes what the receive loop has queued, at most every <see cref="FlushInterval"/>.</summary>
    private async Task FlushLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(FlushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false)) Flush();
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
        catch (ObjectDisposedException)
        {
            // The timer went away underneath us while stopping.
        }
    }

    /// <summary>Hands everything queued to the subscribers, if there is anything.</summary>
    private void Flush()
    {
        List<LogEntry> batch;
        lock (_gate)
        {
            if (_pending.Count == 0) return;
            // Swapped out rather than copied: the receive loop gets an empty list to fill while
            // subscribers work through this one, and the lock is held for neither.
            batch = _pending;
            _pending = new List<LogEntry>();
        }

        EntriesAppended?.Invoke(batch);
        StatusChanged?.Invoke();
    }

    /// <summary>
    /// Stops Windows from failing a receive with <see cref="SocketError.ConnectionReset"/> because
    /// some earlier send drew an ICMP port-unreachable. Windows-only ioctl (SIO_UDP_CONNRESET);
    /// elsewhere the call throws and the behaviour is already what we want, so the receive loop
    /// also handles the error defensively.
    /// </summary>
    private static void DisableConnectionReset(UdpClient socket)
    {
        const int SioUdpConnReset = -1744830452;   // 0x9800000C
        try
        {
            socket.Client.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
        }
        catch (Exception)
        {
            // Not supported on this platform; the catch in the receive loop covers it.
        }
    }

    /// <summary>
    /// The sender as shown in the *Source* column when the event carries no <c>log4japp</c>
    /// property. IPv4-mapped loopback is spelled plainly, since "::ffff:127.0.0.1" is the same
    /// machine and reads like a bug.
    /// </summary>
    private static string Describe(IPEndPoint endpoint)
    {
        var address = endpoint.Address.IsIPv4MappedToIPv6
            ? endpoint.Address.MapToIPv4()
            : endpoint.Address;
        return $"{address}:{endpoint.Port}";
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
