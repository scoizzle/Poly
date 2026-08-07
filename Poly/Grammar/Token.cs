namespace Poly.Grammar;

/// <summary>
/// A single token produced by a <see cref="TokenReader{TKind}"/>. Carries the
/// discriminated kind, the original text, and source position for error reporting.
/// <see cref="Payload"/> is an optional non-text channel for token media that
/// carries richer values (e.g. decoded binaries from a UTF-8 stream reader);
/// text scanners leave it <c>null</c>.
/// </summary>
public readonly record struct Token<TKind>(TKind Kind, string Text, int Line, int Col, object? Payload = null)
    where TKind : struct;