namespace Poly.Grammar;

/// <summary>
/// Source of characters for scanners. Text-based sources (<see cref="StringCharSource"/>)
/// and stream-based sources (UTF-8 readers) implement this so one scanner class can
/// drive multiple backing media.
/// </summary>
public interface ICharSource {
    /// <summary>
    /// Peeks the character at 0-based <paramref name="index"/> without consuming it.
    /// Returns <c>'\0'</c> when <paramref name="index"/> is past the end of the source.
    /// </summary>
    char Peek(int index);

    /// <summary>Total number of characters available in the source.</summary>
    int Length { get; }
}

/// <summary>An <see cref="ICharSource"/> backed by an in-memory string.</summary>
public sealed class StringCharSource : ICharSource {
    private readonly string _text;

    public StringCharSource(string text) {
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public char Peek(int index) => (uint)index < (uint)_text.Length ? _text[index] : '\0';

    public int Length => _text.Length;
}

/// <summary>
/// Base class for character-navigating token readers. Provides character-level
/// navigation (<see cref="PeekChar"/>, <see cref="AdvanceChar"/>), line/column
/// tracking, whitespace skipping, and token construction over an
/// <see cref="ICharSource"/> — independent of whether the source is a string,
/// a UTF-8 stream, or any other character media.
/// </summary>
public abstract class CharSourceTokenReader<TKind> : TokenReader<TKind> where TKind : struct {
    private readonly ICharSource _source;

    /// <summary>Current character position in the source (0-based).</summary>
    public int Position { get; protected set; }

    /// <summary>Current source line (1-based).</summary>
    public int Line { get; protected set; } = 1;

    /// <summary>Current source column (1-based).</summary>
    public int Column { get; protected set; } = 1;

    protected CharSourceTokenReader(ICharSource source) {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>Peeks at the character <paramref name="offset"/> ahead without advancing.</summary>
    protected char PeekChar(int offset = 0) => _source.Peek(Position + offset);

    /// <summary>Advances one character, updating line/column tracking.</summary>
    protected char AdvanceChar() {
        var ch = _source.Peek(Position);
        Position++;
        if (ch == '\n') { Line++; Column = 1; }
        else { Column++; }
        return ch;
    }

    /// <summary>Skips whitespace characters (space, tab, newline, carriage return).</summary>
    protected void SkipWhitespace() {
        while ((uint)Position < (uint)_source.Length) {
            var ch = _source.Peek(Position);
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