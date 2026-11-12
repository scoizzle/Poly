namespace Poly.Grammar;

/// <summary>
/// Language-owned token contract — the only thing the engine requires of a token.
/// Kind is the comparability surface; content and position live on the concrete
/// token type (e.g. text for the DSL, chars for matching, decoded nodes for binary).
/// </summary>
/// <remarks>
/// <see cref="SpanToken{TKind}"/> is the span-form concrete token: kind plus a range into a <see cref="SourceText"/>.
/// </remarks>
public interface IToken<TTokenKind> where TTokenKind : struct {
    /// <summary>The discriminated token kind; the matcher's only comparison surface.</summary>
    TTokenKind Kind { get; }
}