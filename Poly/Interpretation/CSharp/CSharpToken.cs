using Poly.Grammar;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// Language-owned C# token: kind + text + source position. Position is
/// language-owned (the engine knows nothing about it), matching
/// <c>DslToken</c>.
/// </summary>
public readonly record struct CSharpToken(CSharpTokenKind Kind, string Text, int Line, int Col) : IToken<CSharpTokenKind>;