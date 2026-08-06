namespace Poly.Grammar;

/// <summary>
/// Thrown by <see cref="TokenReader{TKind}.Expect"/> and <see cref="Matcher{TKind}"/>
/// when the token stream does not match the expected grammar.
/// </summary>
public sealed class GrammarException : FormatException {
    /// <summary>Source line where the error occurred (1-based).</summary>
    public int Line { get; }

    /// <summary>Source column where the error occurred (1-based).</summary>
    public int Column { get; }

    public GrammarException(string message, int line, int col)
        : base($"{message} (line {line}, col {col})") {
        Line = line;
        Column = col;
    }
}