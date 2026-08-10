using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Language-owned token for the product DSL: kind + text + source position.
/// Position is language-owned per the re-vision — the engine knows nothing about it.
/// </summary>
public readonly record struct DslToken(DslTokenKind Kind, string Text, int Line, int Col) : IToken<DslTokenKind>;