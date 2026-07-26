namespace Poly.Text.Grammar;

/// <summary>
/// Abstract base for all token readers. Provides lookahead buffering,
/// <see cref="Read"/>, <see cref="Peek"/>, and <see cref="Expect"/>.
/// Concrete implementations override <see cref="ScanNextToken"/> to
/// produce tokens from their specific source (string, UTF-8, stream, etc.).
/// </summary>
public abstract class TokenReader<TKind> where TKind : struct {
    private readonly List<Token<TKind>> _buffer = new();

    /// <summary>
    /// Consumes and returns the next token from the source.
    /// </summary>
    public Token<TKind> Read() {
        if (_buffer.Count > 0) {
            var t = _buffer[0];
            _buffer.RemoveAt(0);
            return t;
        }
        return ScanNextToken();
    }

    /// <summary>
    /// Peeks at the nth future token (1-based) without consuming it.
    /// Multiple calls with the same <paramref name="n"/> return the same token.
    /// </summary>
    public Token<TKind> Peek(int n = 1) {
        while (_buffer.Count < n)
            _buffer.Add(ScanNextToken());
        return _buffer[n - 1];
    }

    /// <summary>
    /// Reads the next token and throws <see cref="GrammarException"/> if its
    /// kind does not match <paramref name="kind"/>.
    /// </summary>
    public Token<TKind> Expect(TKind kind) {
        var t = Read();
        if (!EqualityComparer<TKind>.Default.Equals(t.Kind, kind))
            throw new GrammarException($"Expected {kind}, got {t.Kind}", t.Line, t.Col);
        return t;
    }

    /// <summary>
    /// Called by <see cref="Read"/> and <see cref="Peek"/> to produce the
    /// next token from the source. The base class handles lookahead buffering.
    /// </summary>
    protected abstract Token<TKind> ScanNextToken();

    /// <summary>
    /// Returns <c>true</c> if <paramref name="kind"/> represents end-of-file
    /// in this reader. Used by <see cref="Matcher{TKind}"/> to prevent infinite
    /// loops in <see cref="Balanced{TKind}"/> when input ends without a closing
    /// delimiter.
    ///
    /// Default returns <c>false</c> — override in concrete readers that define
    /// an explicit <c>EndOfFile</c> token kind.
    /// </summary>
    public virtual bool IsEndOfFile(TKind kind) => false;
}