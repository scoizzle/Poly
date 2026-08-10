namespace Poly.Grammar;

/// <summary>
/// Internal factory for grammar parse errors — a plain <see cref="FormatException"/>
/// with the position baked into the message when known. No custom type, no structured
/// properties: consumers read <c>Message</c>, and nothing reads structured
/// line/column on parse errors today. If a consumer ever needs to distinguish
/// "grammar error" by type, reintroduce <c>GrammarException : FormatException</c>
/// as a non-breaking additive change.
/// </summary>
internal static class GrammarError {
    public static FormatException Error(string message) => new(message);

    public static FormatException Error(string message, int line, int column) =>
        new($"{message} (line {line}, col {column})");
}