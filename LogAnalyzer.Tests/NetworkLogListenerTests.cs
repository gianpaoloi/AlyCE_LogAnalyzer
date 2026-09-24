using System.Net;
using System.Net.Sockets;
using System.Text;
using LogAnalyzer.Models;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

/// <summary>
/// Round trips over the loopback interface rather than mocking a socket: the point of this service
/// is that a real NLogViewer target can reach it, and the parts that go wrong (reassembly across
/// datagrams, a port already taken, traffic that is not log4j XML) only show up on a real socket.
/// </summary>
public class NetworkLogListenerTests : IAsyncDisposable
{
    private readonly NetworkLogListener _listener = new();
    private readonly List<LogEntry> _seen = new();
    private readonly object _gate = new();

    public NetworkLogListenerTests()
    {
        _listener.EntriesAppended += entries =>
        {
            lock (_gate) _seen.AddRange(entries);
        };
    }

    public async ValueTask DisposeAsync() => await _listener.DisposeAsync();

    /// <summary>A port nothing is bound to right now. Discovered by letting the OS pick one.</summary>
    private static int FreePort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static async Task SendAsync(int port, string payload)
    {
        using var client = new UdpClient();
        await client.SendAsync(Encoding.UTF8.GetBytes(payload), new IPEndPoint(IPAddress.Loopback, port));
    }

    private static string Event(string message, string level = "Info", string logger = "A.B") =>
        $"""
        <log4j:event logger="{logger}" level="{level}" timestamp="1783677600000" thread="1"><log4j:message>{message}</log4j:message></log4j:event>
        """;

    private List<string> Messages()
    {
        lock (_gate) return _seen.Select(e => e.Message).ToList();
    }

    /// <summary>Polls until <paramref name="predicate"/> holds, so the tests don't race the listener.</summary>
    private static async Task<bool> WaitFor(Func<bool> predicate, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(20);
        }

        return predicate();
    }

    [Fact]
    public async Task Receives_an_event_sent_to_the_port()
    {
        var port = FreePort();
        await _listener.StartAsync(port, allInterfaces: false);
        Assert.True(_listener.IsListening, _listener.LastError);
        Assert.Equal($"udp://127.0.0.1:{port}", _listener.Endpoint);

        await SendAsync(port, Event("hello from NLog"));

        Assert.True(await WaitFor(() => Messages().Count == 1), $"got {Messages().Count}");
        Assert.Equal(new[] { "hello from NLog" }, Messages());
        Assert.Equal(1, _listener.TotalEntries);
        Assert.NotNull(_listener.LastActivity);
    }

    [Fact]
    public async Task Receives_several_events_batched_into_one_datagram()
    {
        var port = FreePort();
        await _listener.StartAsync(port, allInterfaces: false);

        await SendAsync(port, Event("one") + Event("two") + Event("three"));

        Assert.True(await WaitFor(() => Messages().Count == 3), $"got {Messages().Count}");
        Assert.Equal(new[] { "one", "two", "three" }, Messages());
    }

    /// <summary>
    /// A payload over the target's maxMessageSize arrives in pieces; the listener has to join them
    /// back up rather than throw both halves away.
    /// </summary>
    [Fact]
    public async Task Reassembles_an_event_split_across_two_datagrams()
    {
        var port = FreePort();
        await _listener.StartAsync(port, allInterfaces: false);

        var whole = Event("split across datagrams");
        var cut = whole.Length / 2;
        // From one socket, so both pieces count as the same sender — which is what makes them
        // joinable. A single UdpClient keeps its source port for both sends.
        using (var client = new UdpClient())
        {
            var to = new IPEndPoint(IPAddress.Loopback, port);
            await client.SendAsync(Encoding.UTF8.GetBytes(whole[..cut]), to);
            await client.SendAsync(Encoding.UTF8.GetBytes(whole[cut..]), to);
        }

        Assert.True(await WaitFor(() => Messages().Count == 1), $"got {Messages().Count}");
        Assert.Equal(new[] { "split across datagrams" }, Messages());
    }

    /// <summary>
    /// "Packets are arriving but the grid is empty" is otherwise indistinguishable from silence, so
    /// the listener counts what it could not use.
    /// </summary>
    [Fact]
    public async Task Counts_datagrams_that_carry_no_event()
    {
        var port = FreePort();
        await _listener.StartAsync(port, allInterfaces: false);

        await SendAsync(port, "GET / HTTP/1.1\r\n\r\n");

        Assert.True(await WaitFor(() => _listener.Ignored == 1), $"ignored {_listener.Ignored}");
        Assert.Empty(Messages());
        Assert.Equal(0, _listener.TotalEntries);
        Assert.Equal(1, _listener.TotalDatagrams);
    }

    /// <summary>
    /// The common setup mistake, and the one that must not be silent: a second copy of the app, or
    /// the real NLog viewer, already has the port.
    /// </summary>
    [Fact]
    public async Task Reports_a_port_that_is_already_taken()
    {
        var port = FreePort();
        using var squatter = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));

        await _listener.StartAsync(port, allInterfaces: false);

        Assert.False(_listener.IsListening);
        Assert.NotNull(_listener.LastError);
        Assert.Contains("in use", _listener.LastError!);
        Assert.Empty(Messages());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public async Task Refuses_a_port_outside_the_valid_range(int port)
    {
        await _listener.StartAsync(port, allInterfaces: false);

        Assert.False(_listener.IsListening);
        Assert.Contains("out of range", _listener.LastError!);
    }

    [Fact]
    public async Task Stopping_releases_the_port_and_the_counters_restart()
    {
        var port = FreePort();
        await _listener.StartAsync(port, allInterfaces: false);
        await SendAsync(port, Event("first"));
        Assert.True(await WaitFor(() => _listener.TotalEntries == 1));

        // Stopping publishes what the flush loop had not got to yet, rather than dropping it.
        await _listener.StopAsync();
        Assert.False(_listener.IsListening);
        Assert.Equal(new[] { "first" }, Messages());

        // The port is free again: something else can take it while we are stopped.
        using (var other = new UdpClient(new IPEndPoint(IPAddress.Loopback, port))) { }

        await _listener.StartAsync(port, allInterfaces: false);
        Assert.True(_listener.IsListening, _listener.LastError);
        Assert.Equal(0, _listener.TotalEntries);

        await SendAsync(port, Event("second"));
        Assert.True(await WaitFor(() => Messages().Count == 2), $"got {Messages().Count}");
        Assert.Equal(new[] { "first", "second" }, Messages());
        Assert.Equal(1, _listener.TotalEntries);
    }

    /// <summary>Events sent while stopped are simply lost — UDP, so nothing queues anywhere.</summary>
    [Fact]
    public async Task Nothing_is_received_while_stopped()
    {
        var port = FreePort();
        await SendAsync(port, Event("into the void"));
        await Task.Delay(100);

        await _listener.StartAsync(port, allInterfaces: false);
        await Task.Delay(300);

        Assert.Empty(Messages());
    }
}
