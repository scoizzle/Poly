namespace Poly.Grammar;

/// <summary>
/// A grammar error with the position that raised it. <see cref="Exception.Message"/> keeps the
/// legacy <c>"… (line L, col C)"</c> text when a position is known; consumers read the
/// structured <see cref="Line"/>, <see cref="Column"/>, <see cref="Offset"/> and
/// <see cref="Length"/> instead of parsing it. Line and column are 1-based; an
/// unknown <see cref="Offset"/> is -1 and an unknown <see cref="Length"/> is 0.
/// </summary>
public sealed class GrammarException : FormatException {
    public GrammarException(string detail)
        : base(detail) {
        Detail = detail;
    }

    public GrammarException(string detail, int line, int column, int offset = -1, int length = 0)
        : base($"{detail} (line {line}, col {column})") {
        Detail = detail;
        Line = line;
        Column = column;
        Offset = offset;
        Length = length;
    }

    /// <summary>The message without the position suffix.</summary>
    public string Detail { get; }

    /// <summary>1-based line, or 0 when the error carries no position.</summary>
    public int Line { get; }

    /// <summary>1-based column, or 0 when the error carries no position.</summary>
    public int Column { get; }

    /// <summary>Zero-based character offset into the text, or -1 when unknown.</summary>
    public int Offset { get; }

    /// <summary>Length of the offending span in characters, or 0 when unknown.</summary>
    public int Length { get; }
}

/// <summary>
/// Internal factory for grammar parse errors. Returns a <see cref="GrammarException"/>,
/// which is a <see cref="FormatException"/>, so existing catch sites keep working.
/// </summary>
internal static class GrammarError {
    public static FormatException Error(string message) => new GrammarException(message);

    public static FormatException Error(string message, int line, int column) =>
        new GrammarException(message, line, column);
}