namespace Poly.Grammar;

/// <summary>
/// Base class for string-backed token readers. Provides character-level
/// navigation (<see cref="PeekChar"/>, <see cref="AdvanceChar"/>), line/column
/// tracking, and whitespace skipping for use by concrete scanner implementations.
/// </summary>
public abstract class StringTokenReader<TKind> : TokenReader<TKind> where TKind : struct {
    /// <summary>The full source text being scanned.</summary>
    public string Text { get; }

    /// <summary>Current character position in <see cref="Text"/>.</summary>
    public int Position { get; protected set; }

    /// <summary>Current source line (1-based).</summary>
    public int Line { get; protected set; } = 1;

    /// <summary>Current source column (1-based).</summary>
    public int Column { get; protected set; } = 1;

    protected StringTokenReader(string text) {
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>Peeks at the character <paramref name="offset"/> ahead without advancing.</summary>
    protected char PeekChar(int offset = 0) {
        var i = Position + offset;
        return i < Text.Length ? Text[i] : '\0';
    }

    /// <summary>Advances one character, updating line/column tracking.</summary>
    protected char AdvanceChar() {
        var ch = Text[Position++];
        if (ch == '\n') { Line++; Column = 1; }
        else { Column++; }
        return ch;
    }

    /// <summary>Skips whitespace characters (space, tab, newline, carriage return).</summary>
    protected void SkipWhitespace() {
        while (Position < Text.Length) {
            var ch = Text[Position];
            if (ch is ' ' or '\t' or '\n' or '\r') { AdvanceChar(); }
            else { break; }
        }
    }

    /// <summary>
    /// Creates a <see cref="Token{TKind}"/> at the current position.
    /// Column is adjusted by <paramref name="text"/>.Length so the recorded
    /// column points to the start of the token.
    /// </summary>
    protected Token<TKind> MakeToken(TKind kind, string text) =>
        new(kind, text, Line, Math.Max(1, Column - text.Length));
}