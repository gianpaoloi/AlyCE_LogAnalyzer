using System.Buffers;

namespace LogAnalyzer.Services;

/// <summary>
/// Splits a stream of UTF-8 bytes into lines, handing out spans into its own buffer instead of
/// allocating a string per line.
/// <para>
/// This replaces <c>File.ReadLines</c> / <c>StreamReader.ReadLine</c> in the load path: the parser
/// works on UTF-8 bytes now, so decoding every line to a string only to have <c>JsonDocument</c>
/// transcode it straight back was two allocations and two passes per line, for no gain. Splitting
/// on bytes is also safe for multi-byte characters — <c>\n</c> and <c>\r</c> cannot appear inside a
/// UTF-8 continuation byte.
/// </para>
/// </summary>
public sealed class Utf8LineReader : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private const int DefaultBufferSize = 128 * 1024;

    /// <summary>
    /// Ceiling on one line. A log with a corrupted binary blob in it has no newline for however
    /// many megabytes, and growing the buffer to hold it would be an easy way to run out of
    /// memory; past this the line is discarded and reading resumes at the next newline.
    /// </summary>
    private const int MaxLineBytes = 8 * 1024 * 1024;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private byte[] _buffer;
    private int _start;
    private int _end;
    private bool _eof;
    private bool _bomChecked;
    private bool _anyLineReturned;
    private bool _skipping;      // discarding the remainder of an over-long line

    /// <summary>Lines dropped for exceeding <see cref="MaxLineBytes"/>.</summary>
    public int OversizedLinesSkipped { get; private set; }

    public Utf8LineReader(Stream stream, bool ownsStream = false, int bufferSize = DefaultBufferSize)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(bufferSize, 4096));
    }

    /// <summary>
    /// Reads the next line, without its trailing CR/LF, as a span into the internal buffer — only
    /// valid until the next call. Returns false at end of stream.
    /// </summary>
    public bool TryReadLine(out ReadOnlySpan<byte> line)
    {
        while (true)
        {
            var available = _buffer.AsSpan(_start, _end - _start);
            var newline = available.IndexOf((byte)'\n');

            if (newline >= 0)
            {
                var raw = available[..newline];
                _start += newline + 1;

                if (_skipping)
                {
                    // Tail of a line we already gave up on; the next one starts clean.
                    _skipping = false;
                    OversizedLinesSkipped++;
                    continue;
                }

                _anyLineReturned = true;
                line = TrimCarriageReturn(raw);
                return true;
            }

            if (_eof)
            {
                if (_skipping)
                {
                    _skipping = false;
                    OversizedLinesSkipped++;
                }

                if (_start >= _end)
                {
                    line = default;
                    return false;
                }

                _anyLineReturned = true;
                line = TrimCarriageReturn(_buffer.AsSpan(_start, _end - _start));
                _start = _end;
                return true;
            }

            Fill();
        }
    }

    /// <summary>
    /// Compacts the unconsumed bytes to the front and reads more. The buffer only grows when a
    /// single line fills it — the common case reuses one pooled array for the whole file.
    /// </summary>
    private void Fill()
    {
        var carried = _end - _start;
        if (_start > 0)
        {
            if (carried > 0) Buffer.BlockCopy(_buffer, _start, _buffer, 0, carried);
            _start = 0;
            _end = carried;
        }

        if (_end == _buffer.Length)
        {
            if (_buffer.Length >= MaxLineBytes)
            {
                // Give up on this line: drop what we have and resume at the next newline.
                _skipping = true;
                _start = _end = 0;
            }
            else
            {
                Grow();
            }
        }

        var read = _stream.Read(_buffer, _end, _buffer.Length - _end);
        if (read <= 0)
        {
            _eof = true;
            return;
        }

        _end += read;
        StripBom();
    }

    private void Grow()
    {
        var bigger = ArrayPool<byte>.Shared.Rent(Math.Min(_buffer.Length * 2, MaxLineBytes));
        Buffer.BlockCopy(_buffer, 0, bigger, 0, _end);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = bigger;
    }

    /// <summary>
    /// A byte-oriented reader has to do this itself — <c>StreamReader</c> used to eat the BOM, and
    /// the parser must not see it on the first line.
    /// </summary>
    private void StripBom()
    {
        if (_bomChecked) return;

        // Only ever at the very start of the stream; if a line has already gone out, there is
        // nothing left to strip.
        if (_anyLineReturned)
        {
            _bomChecked = true;
            return;
        }

        // A first read shorter than the marker is legal; wait for the rest before deciding.
        if (_end - _start < Bom.Length) return;

        _bomChecked = true;
        if (_buffer.AsSpan(_start, Bom.Length).SequenceEqual(Bom)) _start += Bom.Length;
    }

    private static ReadOnlySpan<byte> TrimCarriageReturn(ReadOnlySpan<byte> line) =>
        line.Length > 0 && line[^1] == (byte)'\r' ? line[..^1] : line;

    public void Dispose()
    {
        if (_buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }

        if (_ownsStream) _stream.Dispose();
    }
}
