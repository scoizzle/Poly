namespace Poly.Grammar;

/// <summary>
/// Span-form token: the kind plus a half-open <c>[Start, Start + Length)</c> range into a
/// <see cref="SourceText"/> held by whoever owns the exchange. Carries no source reference, so it is
/// 12 bytes for an int-sized kind (a default enum) and satisfies the kind-only
/// <see cref="IToken{TTokenKind}"/> contract unchanged. The range is not validated here;
/// <see cref="SourceText.Span{TKind}"/> enforces it.
/// </summary>
public readonly record struct SpanToken<TKind>(TKind Kind, int Start, int Length) : IToken<TKind>
    where TKind : struct;