namespace Poly.DomainModeling.Language;

/// <summary>
/// The two text views of a <see cref="DslToken"/> of any kind. Everything else reads a token through
/// its span API: <see cref="DslToken.TextEquals"/>, <see cref="DslToken.Name"/>, <see cref="DslToken.Unescape"/>
/// and <see cref="DslToken.Span"/>, each under a kind the code has checked. <see cref="Word"/> is the one read for a decision on a token of unknown kind.
/// </summary>
internal static class DslTokenText {
    /// <summary>
    /// The token as a message or a read identifier shows it: a string literal's decoded value, an
    /// identifier's interned name, a number's slice, a keyword or punctuation its canonical spelling (which is
    /// its slice, so nothing is copied), and end of input empty. Never throws.
    /// </summary>
    public static string Shown(DslToken token) => token.Kind switch {
        DslTokenKind.StringLiteral => token.Unescape(),
        DslTokenKind.Identifier => token.Name(),
        DslTokenKind.Number => token.Span.ToString(),
        DslTokenKind.EndOfFile => "",
        _ => DslGrammar.CanonicalText(token.Kind),
    };

    /// <summary>
    /// The text a DECISION reads from a token that may or may not be a word: an identifier's name or a keyword's
    /// canonical spelling, and <c>""</c> for every other kind. A string literal is not a word, so
    /// <c>X: "enum"</c> is not an enum and <c>Red "abc"</c> in an enum body is not a reserved word.
    /// </summary>
    public static string Word(DslToken token) => token.Kind switch {
        DslTokenKind.Identifier => token.Name(),
        DslTokenKind.StringLiteral or DslTokenKind.Number or DslTokenKind.EndOfFile => "",
        _ => DslGrammar.CanonicalText(token.Kind) is { Length: > 0 } text && char.IsLetter(text[0]) ? text : "",
    };
}
