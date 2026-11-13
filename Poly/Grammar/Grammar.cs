namespace Poly.Grammar;

/// <summary>
/// An immutable named collection of <see cref="Pattern{TToken,TTokenKind}"/> grouped
/// into rules. Each rule is a context in which a set of patterns are valid; the
/// <see cref="Matcher{TToken,TTokenKind}"/> finds the longest matching pattern at the
/// current position.
///
/// Construct via <see cref="GrammarBuilder{TToken,TTokenKind}"/>. Adding patterns
/// never mutates this instance — <see cref="Extend"/> returns a new table.
/// </summary>
public sealed class Grammar<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly Dictionary<string, List<Pattern<TToken, TTokenKind>>> _rules;

    public Grammar() {
        _rules = new Dictionary<string, List<Pattern<TToken, TTokenKind>>>(StringComparer.Ordinal);
    }

    internal Grammar(Dictionary<string, List<Pattern<TToken, TTokenKind>>> rules) {
        _rules = rules;
    }

    /// <summary>
    /// Mutable copy of this table. Register many patterns, then
    /// <see cref="GrammarBuilder{TToken,TTokenKind}.Build"/> once.
    /// </summary>
    public GrammarBuilder<TToken, TTokenKind> ToBuilder() =>
        GrammarBuilder<TToken, TTokenKind>.From(this);

    /// <summary>
    /// This table plus <paramref name="contribute"/>. This instance is unchanged.
    /// Contributions accumulate on one builder and freeze once.
    /// </summary>
    public Grammar<TToken, TTokenKind> Extend(Action<GrammarBuilder<TToken, TTokenKind>> contribute) {
        ArgumentNullException.ThrowIfNull(contribute);
        var builder = ToBuilder();
        contribute(builder);
        return builder.Build();
    }

    internal IReadOnlyDictionary<string, List<Pattern<TToken, TTokenKind>>> Rules => _rules;

    /// <summary>
    /// Returns all patterns registered under <paramref name="ruleName"/>, sorted by
    /// first-token kind then element count descending (tie-break enumeration only —
    /// the matcher still longest-matches, including Repeat items). Lenient for
    /// unknown rules (empty list) — the matcher validates rule names before matching
    /// (N3) so <see cref="Repeat{TToken,TTokenKind}"/> keeps zero-many-on-unknown
    /// semantics while typo'd rule references fail at the source.
    /// </summary>
    public IReadOnlyList<Pattern<TToken, TTokenKind>> GetPatterns(string ruleName) =>
        _rules.TryGetValue(ruleName, out var list) ? list : [];

    private IReadOnlySet<string>? _committing;

    /// <summary>
    /// True when <paramref name="ruleName"/> holds a <see cref="Cut{TToken,TTokenKind}"/> or a
    /// <see cref="Refuse{TToken,TTokenKind}"/>, directly or through a rule a row references (<c>Ref</c>,
    /// <c>Repeat</c>, a <c>LeftAssoc</c> operand, an optional <c>Ref</c>). Computed once per table.
    /// </summary>
    internal bool CanCommit(string ruleName) {
        var committing = Volatile.Read(ref _committing) ?? ComputeCommitting();
        return committing.Count != 0 && committing.Contains(ruleName);
    }

    private IReadOnlySet<string> ComputeCommitting() {
        var committing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (rule, patterns) in _rules) {
            if (patterns.Exists(p => p.Elements.Any(e => e is Cut<TToken, TTokenKind> or Refuse<TToken, TTokenKind>)))
                committing.Add(rule);
        }
        for (var grew = committing.Count != 0; grew;) {
            grew = false;
            foreach (var (rule, patterns) in _rules) {
                if (!committing.Contains(rule) && patterns.Exists(p => p.Elements.Any(e => References(e) is { } target && committing.Contains(target))))
                    grew |= committing.Add(rule);
            }
        }
        IReadOnlySet<string> frozen = committing.Count == 0 ? NoRules : committing;
        return Interlocked.CompareExchange(ref _committing, frozen, null) ?? frozen;
    }

    private static readonly IReadOnlySet<string> NoRules = new HashSet<string>();

    private static string? References(IPatternElement<TToken, TTokenKind> element) => element switch {
        Ref<TToken, TTokenKind> r => r.RuleName,
        Repeat<TToken, TTokenKind> r => r.RuleName,
        LeftAssoc<TToken, TTokenKind> l => l.OperandRule,
        Optional<TToken, TTokenKind> o => References(o.Inner),
        _ => null,
    };

    /// <summary>True when a rule with this name is defined.</summary>
    public bool HasRule(string ruleName) => _rules.ContainsKey(ruleName);

    /// <summary>All known rule names.</summary>
    public IEnumerable<string> KnownRules => _rules.Keys;

    /// <summary>Named pattern in <paramref name="ruleName"/>, or false if missing.</summary>
    public bool TryGetPattern(string ruleName, string patternName, [NotNullWhen(true)] out Pattern<TToken, TTokenKind>? pattern) {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(patternName);
        foreach (var candidate in GetPatterns(ruleName)) {
            if (string.Equals(candidate.Name, patternName, StringComparison.Ordinal)) {
                pattern = candidate;
                return true;
            }
        }
        pattern = null;
        return false;
    }

    /// <summary>Named pattern. Unknown rule or pattern fails closed.</summary>
    public Pattern<TToken, TTokenKind> GetPattern(string ruleName, string patternName) =>
        TryGetPattern(ruleName, patternName, out var pattern)
            ? pattern
            : throw new ArgumentException($"Unknown pattern '{patternName}' in rule '{ruleName}'");
}