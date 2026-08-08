namespace Poly.Grammar;

/// <summary>
/// A single element in a <see cref="Pattern{TKind}"/>. Each element describes
/// what token sequence it expects at the current scan position.
/// </summary>
public interface IPatternElement<TKind> where TKind : struct;

// ─── Concrete element types ─────────────────────────────────

/// <summary>Matches a single token of a specific <typeparamref name="TKind"/> (fixed syntax — auto-emitted by printer).</summary>
public sealed record MatchToken<TKind>(TKind Kind) : IPatternElement<TKind>
    where TKind : struct;

/// <summary>Matches a single value-bearing token of a specific <typeparamref name="TKind"/> (runtime content — supplied by printer callback).</summary>
public sealed record MatchValue<TKind>(TKind Kind) : IPatternElement<TKind>
    where TKind : struct;

/// <summary>Matches a single token whose kind satisfies a predicate.</summary>
public sealed record MatchPredicate<TKind>(
    Func<TKind, bool> Predicate,
    string Label
) : IPatternElement<TKind>
    where TKind : struct;

/// <summary>Matches zero or one occurrence of an inner element.</summary>
public sealed record Optional<TKind>(IPatternElement<TKind> Inner) : IPatternElement<TKind>
    where TKind : struct;

/// <summary>
/// Matches zero or more occurrences of patterns from the named grammar rule.
/// Used for repeating body constructs (e.g. statements in a block).
/// </summary>
public sealed record ManyOf<TKind>(string RuleName) : IPatternElement<TKind>
    where TKind : struct;

/// <summary>
/// Matches exactly one occurrence of the named grammar rule (recursive /
/// nested languages). Uses the same longest-match selection as
/// <see cref="Matcher{TKind}.TryMatch"/> relative to the current offset;
/// a sub-match that consumes zero tokens is treated as failure (infinite
/// recursion guard).
/// </summary>
public sealed record RuleRef<TKind>(string RuleName) : IPatternElement<TKind>
    where TKind : struct;

/// <summary>
/// Matches a left-associative chain: one <paramref name="OperandRule"/>
/// match, then while an operator kind from <paramref name="OperatorKinds"/>
/// matches, another operand. The full span (operands + operators) is
/// accumulated flat for <see cref="MatchResult{TKind}"/>; folding the chain
/// into IR is the handler's job (operator identity is recoverable from kinds).
/// A trailing operator with no following operand fails the whole element.
/// </summary>
public sealed record LeftAssoc<TKind>(
    string OperandRule,
    IReadOnlyList<TKind> OperatorKinds
) : IPatternElement<TKind>
    where TKind : struct;

/// <summary>
/// Matches brace-balanced content: <paramref name="Open"/> ... <paramref name="Close"/>,
/// tracking nesting depth so the first matching close at depth 0 terminates.
/// </summary>
public sealed record Balanced<TKind>(TKind Open, TKind Close) : IPatternElement<TKind>
    where TKind : struct;

/// <summary>Matches any single token (wildcard).</summary>
public sealed record AnyToken<TKind> : IPatternElement<TKind>
    where TKind : struct;