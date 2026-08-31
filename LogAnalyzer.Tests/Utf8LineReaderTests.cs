using System.Text;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class Utf8LineReaderTests
{
    private static List<string> ReadAll(byte[] bytes, int bufferSize = 128 * 1024)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new Utf8LineReader(stream, bufferSize: bufferSize);

        var lines = new List<string>();
        while (reader.TryReadLine(out var line)) lines.Add(Encoding.UTF8.GetString(line));
        return lines;
    }

    private static List<string> ReadAll(string text, int bufferSize = 128 * 1024) =>
        ReadAll(Encoding.UTF8.GetBytes(text), bufferSize);

    [Fact]
    public void Splits_on_lf() =>
        Assert.Equal(new[] { "a", "b", "c" }, ReadAll("a\nb\nc"));

    [Fact]
    public void Strips_carriage_returns() =>
        Assert.Equal(new[] { "a", "b", "c" }, ReadAll("a\r\nb\r\nc\r\n"));

    [Fact]
    public void A_trailing_newline_does_not_produce_an_empty_last_line() =>
        Assert.Equal(new[] { "a", "b" }, ReadAll("a\nb\n"));

    [Fact]
    public void A_missing_trailing_newline_still_yields_the_last_line() =>
        Assert.Equal(new[] { "a", "b" }, ReadAll("a\nb"));

    [Fact]
    public void Blank_lines_are_preserved() =>
        Assert.Equal(new[] { "a", "", "b" }, ReadAll("a\n\nb"));

    [Fact]
    public void Empty_input_yields_nothing() =>
        Assert.Empty(ReadAll(""));

    [Fact]
    public void Strips_a_byte_order_mark_from_the_first_line()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("first\nsecond")).ToArray();

        Assert.Equal(new[] { "first", "second" }, ReadAll(bytes));
    }

    /// <summary>
    /// The buffer is compacted and grown as needed, so a line longer than the buffer — and a
    /// multi-byte character straddling a refill boundary — must still come out intact.
    /// </summary>
    [Fact]
    public void Lines_longer_than_the_buffer_are_reassembled()
    {
        var longLine = new string('x', 20_000);
        var lines = ReadAll($"short\n{longLine}\nshort2", bufferSize: 4096);

        Assert.Equal(new[] { "short", longLine, "short2" }, lines);
    }

    [Fact]
    public void Multi_byte_characters_survive_a_refill_boundary()
    {
        // 'è' is two bytes and 🚀 is four, so with an awkward buffer size some of them are
        // guaranteed to straddle a read.
        var text = string.Concat(Enumerable.Repeat("èé🚀ü", 2000));
        var lines = ReadAll($"{text}\n{text}", bufferSize: 4096);

        Assert.Equal(new[] { text, text }, lines);
    }

    [Fact]
    public void A_line_read_in_single_byte_chunks_is_still_assembled_correctly()
    {
        using var stream = new DripStream(Encoding.UTF8.GetBytes("hello\nworld\n"));
        using var reader = new Utf8LineReader(stream);

        var lines = new List<string>();
        while (reader.TryReadLine(out var line)) lines.Add(Encoding.UTF8.GetString(line));

        Assert.Equal(new[] { "hello", "world" }, lines);
    }

    [Fact]
    public void A_bom_arriving_one_byte_at_a_time_is_still_stripped()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("first\n")).ToArray();
        using var stream = new DripStream(bytes);
        using var reader = new Utf8LineReader(stream);

        Assert.True(reader.TryReadLine(out var line));
        Assert.Equal("first", Encoding.UTF8.GetString(line));
    }

    /// <summary>Returns one byte per Read, the worst case for buffer management.</summary>
    private sealed class DripStream(byte[] data) : Stream
    {
        private int _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= data.Length || count == 0) return 0;
            buffer[offset] = data[_position++];
            return 1;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
