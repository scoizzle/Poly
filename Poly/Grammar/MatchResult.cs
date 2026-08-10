namespace Poly.Grammar;

/// <summary>
/// The result of a successful pattern match: the pattern name and the full token
/// sequence consumed. Handlers destructure by position or by capture name.
///
/// <see cref="Captures"/> is an empty seam today — it exists so the future
/// <c>Capture</c> / length-reference elements (non-greedy matching, length-prefixed
/// binary) can attach named sub-sequences without changing this result type.
/// </summary>
public sealed class MatchResult<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    public string PatternName { get; }

    /// <summary>All tokens consumed by this match, in order.</summary>
    public IReadOnlyList<TToken> Tokens { get; }

    /// <summary>Number of tokens consumed.</summary>
    public int Consumed => Tokens.Count;

    /// <summary>
    /// Named sub-sequences captured by future Capture elements. Empty until that
    /// element lands — unused today by design (forward-compatible seam).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TToken>> Captures { get; }

    public MatchResult(
        string patternName,
        IReadOnlyList<TToken> tokens,
        IReadOnlyDictionary<string, IReadOnlyList<TToken>>? captures = null) {
        PatternName = patternName;
        Tokens = tokens;
        Captures = captures ?? new Dictionary<string, IReadOnlyList<TToken>>();
    }
}