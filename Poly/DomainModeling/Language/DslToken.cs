using Poly.Grammar;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Language-owned token for the product DSL: kind + text + source position.
/// Position is language-owned per the re-vision — the engine knows nothing about it.
/// <see cref="Line"/>/<see cref="Col"/> are the token start; <see cref="Offset"/>/<see cref="Length"/>
/// are the raw source span (so an escaped or unterminated string is exact).
/// </summary>
public readonly record struct DslToken(DslTokenKind Kind, string Text, int Line, int Col, int Offset = -1, int Length = 0) : IToken<DslTokenKind>;