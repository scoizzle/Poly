namespace Poly.Grammar;

/// <summary>
/// A single element in a <see cref="Pattern{TToken,TTokenKind}"/>: describes what
/// token sequence it expects at the current scan position. Recognition only —
/// folding matches into IR is the handler's job.
/// </summary>
public interface IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>Matches a single token of a specific kind (fixed syntax — auto-emitted by printer).</summary>
public sealed record MatchKind<TToken, TTokenKind>(TTokenKind Kind) : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Matches a single value-bearing token of a specific kind (runtime content).
/// <paramref name="Name"/> is the capture / print-fill key; null is unnamed (legacy callback).
/// </summary>
public sealed record Value<TToken, TTokenKind>(TTokenKind Kind, string? Name = null)
    : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>Matches a single token whose full content satisfies a predicate (semantic predicates live here).</summary>
public sealed record MatchPredicate<TToken, TTokenKind>(
    Func<TToken, bool> Predicate,
    string Label
) : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>Matches zero or one occurrence of an inner element.</summary>
public sealed record Optional<TToken, TTokenKind>(IPatternElement<TToken, TTokenKind> Inner)
    : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Matches between <paramref name="Min"/> and <paramref name="Max"/> occurrences of
/// patterns from the named rule. Bounded repetition by construction — no hard
/// iteration cap. A zero-width sub-match stops the loop (infinite-recursion guard);
/// fewer than <paramref name="Min"/> matches fails the element.
/// </summary>
public sealed record Repeat<TToken, TTokenKind>(
    string RuleName,
    int Min = 0,
    int Max = int.MaxValue
) : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Matches exactly one occurrence of the named rule (recursive / nested languages).
/// A sub-match consuming zero tokens is treated as failure (infinite-recursion guard).
/// </summary>
public sealed record Ref<TToken, TTokenKind>(string RuleName) : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Matches a left-associative chain: one <paramref name="OperandRule"/> match, then
/// while an operator kind from <paramref name="OperatorKinds"/> matches, another
/// operand. The full span (operands + operators) is accumulated flat; folding into
/// IR is the handler's job (operator identity recoverable from kinds). A trailing
/// operator with no following operand fails the whole element.
/// </summary>
public sealed record LeftAssoc<TToken, TTokenKind>(
    string OperandRule,
    IReadOnlyList<TTokenKind> OperatorKinds
) : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Matches brace-balanced content. The token at the current position MUST be
/// <paramref name="Open"/> — Balanced does not scan forward past leading content
/// (that is the enclosing pattern's job). Nesting depth is tracked so the first
/// matching <paramref name="Close"/> at depth 0 terminates the span. End-of-stream
/// before the close fails the element.
/// </summary>
public sealed record Balanced<TToken, TTokenKind>(TTokenKind Open, TTokenKind Close)
    : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>Matches any single token (wildcard).</summary>
public sealed record Any<TToken, TTokenKind> : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;