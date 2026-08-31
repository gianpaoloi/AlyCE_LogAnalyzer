namespace LogAnalyzer.Services;

/// <summary>
/// Turns a concrete log message into a stable "signature" by masking the parts that vary
/// between otherwise-identical events (guids, numbers, durations, quoted values, tenant ids).
/// Used to cluster similar WARN/ERROR entries together in the triage view.
/// <para>
/// This was seven chained <c>Regex.Replace</c> calls, each allocating an intermediate string for
/// the whole message. It is now a single left-to-right scan that writes straight into one buffer:
/// at each position the longest of the recognised shapes wins, in the same priority order the
/// regex chain applied. <c>MessageNormalizerTests</c> pins the output against the original regex
/// pipeline over the sample logs — the two can only diverge on inputs where a 16+ hex-digit run
/// is immediately followed by the tail of a guid, which the chain masked as a guid first and this
/// scan masks as hex. Both mask the varying part, so clustering is unaffected either way.
/// </para>
/// </summary>
public static class MessageNormalizer
{
    private const int MaxLength = 400;

    private const string GuidMask = "{GUID}";
    private const string DateMask = "{DATE}";
    private const string DurationMask = "{DURATION}";
    private const string HexMask = "{HEX}";
    private const string NumberMask = "{N}";
    private const string QuotedMask = "'{VAL}'";

    /// <summary>Shortest run of hex digits treated as an opaque id rather than a number.</summary>
    private const int MinHexRun = 16;

    /// <summary>
    /// Masks the varying parts of <paramref name="humanPart"/>, which is expected to be the
    /// message with its stack trace and CRLF tail already removed (see <c>LogEntry.Signature</c>).
    /// </summary>
    public static string Signature(ReadOnlySpan<char> humanPart)
    {
        if (humanPart.IsEmpty) return "";

        // The result only ever shrinks or stays close to the input: masks are short, and the
        // whole thing is truncated to MaxLength anyway.
        var buffer = new System.Text.StringBuilder(Math.Min(humanPart.Length, MaxLength) + 16);

        var i = 0;
        while (i < humanPart.Length && buffer.Length <= MaxLength)
        {
            var c = humanPart[i];

            if (char.IsWhiteSpace(c))
            {
                // Collapse any run of whitespace to a single space; leading whitespace is
                // dropped because the result is trimmed at the end anyway.
                while (i < humanPart.Length && char.IsWhiteSpace(humanPart[i])) i++;
                buffer.Append(' ');
                continue;
            }

            if (c == '\'' && TryQuoted(humanPart, i, out var quotedLength))
            {
                buffer.Append(QuotedMask);
                i += quotedLength;
                continue;
            }

            if (IsHexDigit(c) && TryMaskedToken(humanPart, i, out var mask, out var tokenLength))
            {
                buffer.Append(mask);
                i += tokenLength;
                continue;
            }

            buffer.Append(c);
            i++;
        }

        // Trim() here rather than on the span: the collapsed whitespace can leave one space at
        // either end that wasn't there in the input.
        var result = buffer.ToString().Trim();
        return result.Length > MaxLength ? result[..MaxLength] : result;
    }

    /// <summary>
    /// Recognises the shapes that stand for a varying value, in the priority the regex chain
    /// used: guid, then duration, then date, then a long hex run, then a plain number.
    /// </summary>
    private static bool TryMaskedToken(ReadOnlySpan<char> s, int start, out string mask, out int length)
    {
        if (TryGuid(s, start, out length)) { mask = GuidMask; return true; }
        if (TryDuration(s, start, out length)) { mask = DurationMask; return true; }
        if (TryDate(s, start, out length)) { mask = DateMask; return true; }
        if (TryHexRun(s, start, out length)) { mask = HexMask; return true; }
        if (TryNumber(s, start, out length)) { mask = NumberMask; return true; }

        mask = "";
        length = 0;
        return false;
    }

    /// <summary>8-4-4-4-12 hex digits. Matched at any offset, like the original regex.</summary>
    private static bool TryGuid(ReadOnlySpan<char> s, int start, out int length)
    {
        length = 0;
        Span<int> groups = [8, 4, 4, 4, 12];
        var i = start;
        for (var g = 0; g < groups.Length; g++)
        {
            if (g > 0)
            {
                if (i >= s.Length || s[i] != '-') return false;
                i++;
            }
            for (var n = 0; n < groups[g]; n++)
            {
                if (i >= s.Length || !IsHexDigit(s[i])) return false;
                i++;
            }
        }

        length = i - start;
        return true;
    }

    /// <summary><c>h:mm:ss</c> or <c>hh:mm:ss</c>, with an optional fractional part.</summary>
    private static bool TryDuration(ReadOnlySpan<char> s, int start, out int length)
    {
        length = 0;
        if (!AtWordStart(s, start)) return false;

        var i = start;
        var hours = DigitRun(s, i);
        if (hours is < 1 or > 2) return false;
        i += hours;

        for (var part = 0; part < 2; part++)
        {
            if (i >= s.Length || s[i] != ':') return false;
            i++;
            if (DigitRun(s, i) < 2) return false;
            i += 2;
        }

        if (i < s.Length && s[i] == '.')
        {
            var fraction = DigitRun(s, i + 1);
            if (fraction > 0) i += 1 + fraction;
        }

        if (!AtWordEnd(s, i)) return false;
        length = i - start;
        return true;
    }

    /// <summary><c>yyyy-MM-dd</c>, <c>dd/MM/yyyy</c> and the other separator/width combinations.</summary>
    private static bool TryDate(ReadOnlySpan<char> s, int start, out int length)
    {
        length = 0;
        if (!AtWordStart(s, start)) return false;

        var i = start;
        var first = DigitRun(s, i);
        if (first is < 2 or > 4) return false;
        i += first;

        if (i >= s.Length || (s[i] != '-' && s[i] != '/')) return false;
        var separator = s[i];
        i++;

        var second = DigitRun(s, i);
        if (second is < 1 or > 2) return false;
        i += second;

        if (i >= s.Length || s[i] != separator) return false;
        i++;

        var third = DigitRun(s, i);
        if (third is < 1 or > 4) return false;
        i += third;

        if (!AtWordEnd(s, i)) return false;
        length = i - start;
        return true;
    }

    /// <summary>A whole word of <see cref="MinHexRun"/> or more hex digits — a hash or an opaque id.</summary>
    private static bool TryHexRun(ReadOnlySpan<char> s, int start, out int length)
    {
        length = 0;
        if (!AtWordStart(s, start)) return false;

        var i = start;
        while (i < s.Length && IsHexDigit(s[i])) i++;
        if (i - start < MinHexRun || !AtWordEnd(s, i)) return false;

        length = i - start;
        return true;
    }

    /// <summary>Any run of digits. Unlike the others this ignores word boundaries, as the regex did.</summary>
    private static bool TryNumber(ReadOnlySpan<char> s, int start, out int length)
    {
        length = DigitRun(s, start);
        return length > 0;
    }

    /// <summary>A single-quoted run. Unterminated quotes are left alone, like the regex.</summary>
    private static bool TryQuoted(ReadOnlySpan<char> s, int start, out int length)
    {
        var close = s[(start + 1)..].IndexOf('\'');
        length = close < 0 ? 0 : close + 2;
        return close >= 0;
    }

    private static int DigitRun(ReadOnlySpan<char> s, int start)
    {
        var i = start;
        while (i < s.Length && s[i] is >= '0' and <= '9') i++;
        return i - start;
    }

    /// <summary>The <c>\b</c> the original patterns anchored on: no word character before the token.</summary>
    private static bool AtWordStart(ReadOnlySpan<char> s, int i) => i == 0 || !IsWordChar(s[i - 1]);

    /// <summary>The trailing <c>\b</c>: no word character after the token.</summary>
    private static bool AtWordEnd(ReadOnlySpan<char> s, int i) => i >= s.Length || !IsWordChar(s[i]);

    private static bool IsWordChar(char c) => c == '_' || char.IsLetterOrDigit(c);

    private static bool IsHexDigit(char c) =>
        c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
