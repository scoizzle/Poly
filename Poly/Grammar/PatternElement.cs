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
/// Matches brace-balanced content: <paramref name="Open"/> ... <paramref name="Close"/>,
/// tracking nesting depth so the first matching close at depth 0 terminates.
/// </summary>
public sealed record Balanced<TKind>(TKind Open, TKind Close) : IPatternElement<TKind>
    where TKind : struct;

/// <summary>Matches any single token (wildcard).</summary>
public sealed record AnyToken<TKind> : IPatternElement<TKind>
    where TKind : struct;