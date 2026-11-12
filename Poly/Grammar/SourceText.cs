namespace Poly.Grammar;

/// <summary>
/// Immutable source buffer with its position surface: line/column from an offset and span slicing.
/// Line/column is the DSL scanner's rule, not a new one: only <c>'\n'</c> ends a line (<c>'\r'</c> is a
/// column character), both are 1-based, and <c>GetLineColumn(Length)</c> is the end-of-input position.
/// A leading <c>'﻿'</c> is not a document character: offsets in <c>[0, origin)</c> return (1,1), the
/// single documented clamp. Offsets stay absolute into the caller's string. Everything outside
/// <c>[0, Length]</c> throws. The line table is built lazily in one O(n) pass; two threads may both build
/// the identical table (reference assignment is atomic) - that benign race is the only concurrency promise.
/// </summary>
public sealed class SourceText {
    private readonly int _origin;
    private int[]? _lineStarts;

    public SourceText(string text) {
        ArgumentNullException.ThrowIfNull(text);
        Text = text.AsMemory();
        _origin = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
    }

    public ReadOnlyMemory<char> Text { get; }

    /// <summary>Per-document intern table; a second <see cref="SourceText"/> over the same text has its own.</summary>
    public NameTable Names { get; } = new();

    public int Length => Text.Length;

    /// <summary>
    /// 1-based line and column of <paramref name="offset"/>: <c>Col = offset - lineStart + 1</c>, where
    /// <c>lineStart</c> is the index after the last <c>'\n'</c> before <paramref name="offset"/>, or the BOM
    /// origin on line 1.
    /// </summary>
    public (int Line, int Col) GetLineColumn(int offset) {
        if ((uint)offset > (uint)Length)
            throw new ArgumentOutOfRangeException(nameof(offset), offset, $"Offset must be within 0..{Length}.");
        if (offset < _origin) return (1, 1);

        var starts = _lineStarts ??= BuildLineStarts();
        // starts[k] is the offset where line k+2 begins; count of starts <= offset = zero-based line index.
        var index = Array.BinarySearch(starts, offset);
        var before = index >= 0 ? index + 1 : ~index;
        var lineStart = before == 0 ? _origin : starts[before - 1];
        return (before + 1, offset - lineStart + 1);
    }

    public ReadOnlySpan<char> Span<TKind>(SpanToken<TKind> token) where TKind : struct {
        if ((uint)token.Start > (uint)Length || (uint)token.Length > (uint)(Length - token.Start))
            throw new ArgumentOutOfRangeException(nameof(token), token, $"Span [{token.Start}, +{token.Length}) is outside 0..{Length}.");
        return Text.Span.Slice(token.Start, token.Length);
    }

    /// <summary>The token's raw slice (string literals keep their quotes and escapes), interned per document.</summary>
    public string Name<TKind>(SpanToken<TKind> token) where TKind : struct => Names.Intern(Span(token));

    private int[] BuildLineStarts() {
        var span = Text.Span;
        var count = 0;
        foreach (var ch in span) if (ch == '\n') count++;
        var starts = new int[count];
        var next = 0;
        for (var i = 0; i < span.Length; i++)
            if (span[i] == '\n') starts[next++] = i + 1;
        return starts;
    }
}