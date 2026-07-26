namespace Poly.Text.Grammar;

/// <summary>
/// A single token produced by a <see cref="TokenReader{TKind}"/>. Carries the
/// discriminated kind, the original text, and source position for error reporting.
/// </summary>
public readonly record struct Token<TKind>(TKind Kind, string Text, int Line, int Col)
    where TKind : struct;