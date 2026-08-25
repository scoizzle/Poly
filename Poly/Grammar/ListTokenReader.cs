namespace Poly.Grammar;

/// <summary>
/// Peek/consume over an already-decoded token list. The last token must be the
/// language's end-of-stream marker.
/// </summary>
public sealed class ListTokenReader<TToken, TTokenKind> : ITokenReader<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly IReadOnlyList<TToken> _tokens;
    private readonly Func<TTokenKind, bool> _endOfStream;
    private int _index;

    public ListTokenReader(IReadOnlyList<TToken> tokens, Func<TTokenKind, bool> endOfStream) {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(endOfStream);
        if (tokens.Count == 0)
            throw new ArgumentException("Token list must include an end-of-stream token.", nameof(tokens));
        _tokens = tokens;
        _endOfStream = endOfStream;
    }

    public TToken Peek(int offset = 0) {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var i = _index + offset;
        return i >= _tokens.Count ? _tokens[^1] : _tokens[i];
    }

    public void Consume(int count) {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _index = Math.Min(_index + count, _tokens.Count);
    }

    public bool EndOfStream(TTokenKind kind) => _endOfStream(kind);
}