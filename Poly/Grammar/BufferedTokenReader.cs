namespace Poly.Grammar;

/// <summary>
/// Shared buffered base: owns the lookahead buffer. <see cref="Peek"/> scans ahead
/// (buffering); <see cref="Consume"/> trims consumed tokens from the front, so the
/// committed position is always the buffer head. Languages implement only
/// <see cref="ScanNextToken"/>.
/// </summary>
public abstract class BufferedTokenReader<TToken, TTokenKind> : ITokenStreamReader<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly List<TToken> _buffer = [];

    public TToken Peek(int offset = 0) {
        while (_buffer.Count <= offset)
            _buffer.Add(ScanNextToken());
        return _buffer[offset];
    }

    public void Consume(int count) {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _buffer.RemoveRange(0, Math.Min(count, _buffer.Count));
    }

    /// <summary>The single physical-decoding seam — languages produce tokens here.</summary>
    protected abstract TToken ScanNextToken();

    public abstract bool EndOfStream(TTokenKind kind);
}