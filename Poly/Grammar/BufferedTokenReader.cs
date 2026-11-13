namespace Poly.Grammar;

/// <summary>
/// Shared buffered base: owns the lookahead buffer. <see cref="Peek"/> scans ahead
/// (buffering); <see cref="Consume"/> trims consumed tokens from the front, so the
/// committed position is always the buffer head. Languages implement only
/// <see cref="ScanNextToken"/>.
/// </summary>
public abstract class BufferedTokenReader<TToken, TTokenKind> : ITokenReader<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly List<TToken> _buffer = [];

    public TToken Peek(int offset = 0) {
        while (_buffer.Count <= offset)
            _buffer.Add(ScanNextToken());
        return _buffer[offset];
    }

    /// <summary>
    /// Commits <paramref name="count"/> tokens, scanning any not yet buffered first, so a Consume before the first Peek
    /// commits exactly as many. Past the end of input the scanner keeps yielding end-of-input tokens, so the head after
    /// consuming past the end is end of input, as a Peek past the end is.
    /// </summary>
    public void Consume(int count) {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > _buffer.Count)
            Peek(count - 1);
        _buffer.RemoveRange(0, count);
    }

    /// <summary>The single physical-decoding seam — languages produce tokens here.</summary>
    protected abstract TToken ScanNextToken();

    public abstract bool EndOfStream(TTokenKind kind);
}