namespace Poly.Grammar;

/// <summary>
/// Stream contract for the matcher (Pipelines-style examine/consume model).
///
/// The reader owns its committed position: the matcher PEEKS tokens relative to
/// that position without consuming; callers COMMIT matched spans via
/// <see cref="Consume"/>. There is no external head-token dance — no Unread, no
/// caller-held `_current` cursor. Scanning ahead buffers tokens until consumed.
///
/// Languages implement the single physical-decoding seam
/// (<see cref="ScanNextToken"/>) on <see cref="BufferedTokenReader{TToken,TTokenKind}"/>;
/// they never reimplement buffering or position tracking.
/// </summary>
public interface ITokenStreamReader<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    /// <summary>
    /// Peeks the token <paramref name="offset"/> positions ahead of the committed
    /// position (0 = the head, 1 = the next, …) without consuming it.
    /// </summary>
    TToken Peek(int offset = 0);

    /// <summary>
    /// Advances the committed position by <paramref name="count"/> tokens, discarding
    /// the consumed span. The next <see cref="Peek"/>(0) is the first unconsumed token.
    /// </summary>
    void Consume(int count);

    /// <summary>
    /// True when <paramref name="kind"/> represents end-of-stream in this reader.
    /// Kind-based (not file-specific); used to prevent infinite loops in
    /// <see cref="Balanced{TToken,TTokenKind}"/> when input ends early.
    /// </summary>
    bool EndOfStream(TTokenKind kind);
}