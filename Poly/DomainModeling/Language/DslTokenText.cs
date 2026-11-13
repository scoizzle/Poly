namespace Poly.DomainModeling.Language;

/// <summary>
/// The two total text views of a <see cref="DslToken"/> of any kind. Everything else reads a token through
/// its span API: <see cref="DslToken.TextEquals"/>, <see cref="DslToken.Name"/>, <see cref="DslToken.Unescape"/>
/// and <see cref="DslToken.Span"/>, each under a kind the code has checked.
/// </summary>
internal static class DslTokenText {
    /// <summary>
    /// The token as a message or a read identifier shows it: a string literal's decoded value, an
    /// identifier's interned name, anything else its source slice (a keyword or punctuation is its canonical
    /// spelling; end of input is empty). Never throws.
    /// </summary>
    public static string Shown(DslToken token) => token.Kind switch {
        DslTokenKind.StringLiteral => token.Unescape(),
        DslTokenKind.Identifier => token.Name(),
        _ => token.Span.ToString(),
    };

    /// <summary>
    /// The text a DECISION reads from a token whose kind the code does not check: the same value as
    /// <see cref="Shown"/>, so a string literal still reads as its decoded value (<c>X: "enum"</c> classifies
    /// as an enum, <c>Red "abc"</c> in an enum body is a reserved word). Kept by name so every such site stays
    /// greppable for the fix; <c>TokenTextReaderTests</c> pins the call sites.
    /// </summary>
    public static string LegacyText(DslToken token) => Shown(token);
}