namespace Poly.Grammar;

/// <summary>
/// A successful rule match: which pattern won, the concatenated token span, leaf
/// captures on this node, and nested rule matches as a form tree.
///
/// Handlers fold the tree (or destructure <see cref="Tokens"/> / <see cref="Captures"/>
/// by position). The engine does not produce product IR.
/// </summary>
public sealed class MatchResult<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    /// <summary>Rule this node matched.</summary>
    public string RuleName { get; }

    public string PatternName { get; }

    /// <summary>All tokens consumed by this match, in order (the span to <c>Consume</c>).</summary>
    public IReadOnlyList<TToken> Tokens { get; }

    /// <summary>Number of tokens consumed.</summary>
    public int Consumed => Tokens.Count;

    /// <summary>
    /// Named leaf captures on <em>this</em> pattern (<see cref="Value{TToken,TTokenKind}"/> /
    /// <see cref="MatchPredicate{TToken,TTokenKind}"/>). Nested captures stay on
    /// <see cref="Children"/> — they do not bubble.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TToken>> Captures { get; }

    /// <summary>
    /// Nested rule matches, left to right: each <see cref="Ref{TToken,TTokenKind}"/>,
    /// each <see cref="Repeat{TToken,TTokenKind}"/> item, and LeftAssoc operands.
    /// </summary>
    public IReadOnlyList<MatchResult<TToken, TTokenKind>> Children { get; }

    /// <summary>
    /// LeftAssoc operator tokens. When this pattern is a single
    /// <see cref="LeftAssoc{TToken,TTokenKind}"/>, <c>Operators.Count == Children.Count - 1</c>.
    /// Empty otherwise.
    /// </summary>
    public IReadOnlyList<TToken> Operators { get; }

    public MatchResult(
        string patternName,
        IReadOnlyList<TToken> tokens,
        IReadOnlyDictionary<string, IReadOnlyList<TToken>>? captures = null,
        string ruleName = "",
        IReadOnlyList<MatchResult<TToken, TTokenKind>>? children = null,
        IReadOnlyList<TToken>? operators = null) {
        PatternName = patternName;
        Tokens = tokens;
        Captures = captures ?? new Dictionary<string, IReadOnlyList<TToken>>(StringComparer.Ordinal);
        RuleName = ruleName ?? "";
        Children = children ?? [];
        Operators = operators ?? [];
    }
}