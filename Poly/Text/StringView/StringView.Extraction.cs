namespace Poly;

public partial struct StringView {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly StringView? ExtractUntil(char value)
    {
        var (str, index, lastIndex) = this;

        int idx = index;
        if (!str.Goto(ref idx, lastIndex, value))
            return default;

        return new StringView(str, index, idx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TryExtractUntil(char value, out StringView slice)
    {
        var (str, index, lastIndex) = this;

        int idx = index;
        if (!str.Goto(ref idx, lastIndex, value)) {
            slice = default;
            return false;
        }

        slice = new StringView(str, index, idx);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly StringView? ExtractUntilAny(ReadOnlySpan<char> values)
    {
        var (str, index, lastIndex) = this;

        int idx = index;
        if (!str.GotoAny(ref idx, lastIndex, values))
            return default;

        return new StringView(str, index, idx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TryExtractUntilAny(ReadOnlySpan<char> values, out StringView slice)
    {
        var (str, index, lastIndex) = this;

        int idx = index;
        if (!str.GotoAny(ref idx, lastIndex, values)) {
            slice = default;
            return false;
        }

        slice = new StringView(str, index, idx);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StringView? ExtractAndConsumeUntil(char value)
    {
        var (str, index, lastIndex) = this;

        int idx = index;
        if (!str.GotoAndConsume(ref idx, lastIndex, value))
            return default;

        Index = idx;
        return new StringView(str, index, idx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StringView? ExtractAndConsumeUntilAny(params char[] values)
    {
        var (str, index, lastIndex) = this;

        int idx = index;
        if (!str.GotoAndConsumeAny(ref idx, lastIndex, values))
            return default;

        Index = idx;
        return new StringView(str, index, idx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ExtractBetween(
        char open,
        char close,
        out StringView section,
        bool includeBraces = false)
    {
        var (str, index, lastIndex) = this;

        var idx = str.FindMatchingBracket(index, lastIndex, open, close);

        if (idx == -1) {
            section = default;
            return false;
        }

        if (includeBraces)
            idx++;
        else
            index++;

        section = new(str, index, idx);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ExtractAndConsumeBetween(
        char open,
        char close,
        out StringView section,
        bool includeBraces = false)
    {
        var (str, index, lastIndex) = this;

        var idx = str.FindMatchingBracket(index, lastIndex, open, close);

        if (idx == -1) {
            section = default;
            return false;
        }

        if (includeBraces) {
            Index = ++idx;
        }
        else {
            index++;
            Index = idx + 1;
        }

        section = new(str, index, idx);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ExtractStringLiteral(
        out StringView section,
        bool includeQuotes = false)
    {
        return ExtractBetween('"', '"', out section, includeQuotes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ExtractAndConsumeStringLiteral(
        out StringView section,
        bool includeQuotes = false)
    {
        return ExtractAndConsumeBetween('"', '"', out section, includeQuotes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TryParse(out bool value)
    {
        if (!BoundsCheck()) {
            value = default;
            return false;
        }

        var (str, idx, lastIndex) = this;

        if (str.IsAt(idx, lastIndex, bool.TrueString, StringComparison.OrdinalIgnoreCase)) {
            value = true;
            return true;
        }
        else
        if (str.IsAt(idx, lastIndex, bool.FalseString, StringComparison.OrdinalIgnoreCase)) {
            value = false;
            return true;
        }

        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TryParse<T>(
        [NotNullWhen(returnValue: true)] out T? value,
        IFormatProvider? formatProvider = default)
            where T : ISpanParsable<T>
    {
        if (!BoundsCheck()) {
            value = default;
            return false;
        }

        var span = String.AsSpan(Index, Length);

        return T.TryParse(span, formatProvider, out value);
    }
}