namespace Poly.Text.Grammar;

/// <summary>
/// The result of a successful pattern match. Carries the pattern name and
/// the full sequence of tokens consumed, so callers can destructure by
/// position or pattern.
/// </summary>
public sealed class MatchResult<TKind> where TKind : struct {
    /// <summary>The name of the pattern that matched.</summary>
    public string PatternName { get; }

    /// <summary>All tokens consumed by this match, in order.</summary>
    public IReadOnlyList<Token<TKind>> Tokens { get; }

    /// <summary>Number of tokens consumed.</summary>
    public int Consumed => Tokens.Count;

    public MatchResult(string patternName, IReadOnlyList<Token<TKind>> tokens) {
        PatternName = patternName;
        Tokens = tokens;
    }
}