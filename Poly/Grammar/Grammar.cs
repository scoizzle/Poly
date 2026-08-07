namespace Poly.Grammar;

/// <summary>
/// A grammar is a named collection of <see cref="Pattern{TKind}"/> grouped
/// into rules. Each rule represents a context in which a set of patterns are
/// valid. The <see cref="Matcher{TKind}"/> uses the grammar to find the longest
/// matching pattern at the current position in a token stream.
///
/// <code>
/// var g = new Grammar&lt;MyKind&gt;();
/// g.Define("entity-body")
///     .Pattern("property").Token(Identifier).Token(Colon).Predicate(IsType, "type").Commit()
///     .Pattern("stage")   .Token(Identifier).Token(Colon).Token(Stage).Balanced(LBrace, RBrace).Commit();
/// </code>
/// </summary>
public sealed class Grammar<TKind> where TKind : struct {
    private readonly Dictionary<string, List<Pattern<TKind>>> _rules = new();

    /// <summary>
    /// Begins defining patterns in a named rule group.
    /// </summary>
    public RuleBuilder<TKind> Define(string ruleName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        return new RuleBuilder<TKind>(this, ruleName);
    }

    /// <summary>
    /// Returns all patterns registered under <paramref name="ruleName"/>,
    /// sorted by first-token kind then by element count descending,
    /// or an empty list if the rule is unknown.
    /// Read-only at match time (sorting happens on <see cref="AddPattern"/>)
    /// so concurrent matchers may share one grammar safely.
    /// </summary>
    public IReadOnlyList<Pattern<TKind>> GetPatterns(string ruleName) {
        if (!_rules.TryGetValue(ruleName, out var list))
            return [];
        return list;
    }

    /// <summary>Returns all known rule names.</summary>
    public IEnumerable<string> KnownRules => _rules.Keys;

    internal void AddPattern(string ruleName, Pattern<TKind> pattern) {
        if (!_rules.TryGetValue(ruleName, out var list))
            _rules[ruleName] = list = new();
        list.Add(pattern);
        // Keep lists sorted after every commit so GetPatterns never mutates
        // under concurrent Matcher use (product parsers share a static table).
        SortPatterns(list);
    }

    // ═══════════════════════════════════════════════════════
    //  Pattern sorting
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Sorts patterns so that for each distinct first-token kind the longest
    /// pattern appears first. Patterns without a <see cref="MatchToken{TKind}"/>
    /// or <see cref="MatchValue{TKind}"/> first element sort after all token-led
    /// patterns.
    ///
    /// This makes <c>ManyOf</c> safe — the first match within a token group
    /// is always the longest potential match.
    /// </summary>
    private void SortPatterns(List<Pattern<TKind>> patterns) {
        patterns.Sort((a, b) => {
            var aIsMatch = a.Elements.Count > 0 && (a.Elements[0] is MatchToken<TKind> or MatchValue<TKind>);
            var bIsMatch = b.Elements.Count > 0 && (b.Elements[0] is MatchToken<TKind> or MatchValue<TKind>);

            // Non-MatchToken-led patterns sort last
            if (aIsMatch != bIsMatch)
                return bIsMatch.CompareTo(aIsMatch);

            // Within same first-token group, sort by element count descending
            if (aIsMatch && bIsMatch) {
                var aKind = FirstKind(a.Elements[0]);
                var bKind = FirstKind(b.Elements[0]);
                var cmp = Comparer<TKind>.Default.Compare(aKind, bKind);
                if (cmp != 0) return cmp;
            }

            // Longer patterns first
            return b.Elements.Count.CompareTo(a.Elements.Count);
        });
    }

    private static TKind FirstKind(IPatternElement<TKind> element) => element switch {
        MatchToken<TKind> mt => mt.Kind,
        MatchValue<TKind> mv => mv.Kind,
        _ => default,
    };
}

/// <summary>
/// Fluent builder returned by <see cref="Grammar{TKind}.Define"/>.
/// Accumulates patterns under a named rule.
/// </summary>
public sealed class RuleBuilder<TKind> where TKind : struct {
    internal Grammar<TKind> Grammar => _grammar;
    internal string RuleName => _ruleName;

    private readonly Grammar<TKind> _grammar;
    private readonly string _ruleName;

    internal RuleBuilder(Grammar<TKind> grammar, string ruleName) {
        _grammar = grammar;
        _ruleName = ruleName;
    }

    /// <summary>Begins defining a new pattern under this rule.</summary>
    public PatternBuilder<TKind> Pattern(string name) =>
        new(this, _grammar, _ruleName, name);
}

/// <summary>
/// Fluent builder returned by <see cref="RuleBuilder{TKind}.Pattern"/>.
/// Accumulates elements and commits the pattern to the grammar.
/// </summary>
public sealed class PatternBuilder<TKind> where TKind : struct {
    private readonly RuleBuilder<TKind> _parent;
    private readonly Grammar<TKind> _grammar;
    private readonly string _ruleName;
    private readonly string _name;
    private readonly List<IPatternElement<TKind>> _elements = new();

    internal PatternBuilder(RuleBuilder<TKind> parent, Grammar<TKind> grammar, string ruleName, string name) {
        _parent = parent;
        _grammar = grammar;
        _ruleName = ruleName;
        _name = name;
    }

    /// <summary>Appends a token of a specific kind (fixed syntax).</summary>
    public PatternBuilder<TKind> Token(TKind kind) {
        _elements.Add(new MatchToken<TKind>(kind));
        return this;
    }

    /// <summary>Appends a value-bearing token of a specific kind (runtime content supplied by printer callback).</summary>
    public PatternBuilder<TKind> Value(TKind kind) {
        _elements.Add(new MatchValue<TKind>(kind));
        return this;
    }

    /// <summary>Appends a predicate-based token match with a human-readable label.</summary>
    public PatternBuilder<TKind> Predicate(Func<TKind, bool> predicate, string label) {
        _elements.Add(new MatchPredicate<TKind>(predicate, label));
        return this;
    }

    /// <summary>Appends an optional token of a specific kind.</summary>
    public PatternBuilder<TKind> Optional(TKind kind) {
        _elements.Add(new Optional<TKind>(new MatchToken<TKind>(kind)));
        return this;
    }

    /// <summary>Appends an optional compound element (any <see cref="IPatternElement{TKind}"/>).</summary>
    public PatternBuilder<TKind> Optional(IPatternElement<TKind> element) {
        _elements.Add(new Optional<TKind>(element));
        return this;
    }

    /// <summary>
    /// Appends a repeat: zero or more matches of patterns from the named rule.
    /// </summary>
    public PatternBuilder<TKind> Many(string ruleName) {
        _elements.Add(new ManyOf<TKind>(ruleName));
        return this;
    }

    /// <summary>
    /// Appends a brace-balanced block: <paramref name="open"/> ... <paramref name="close"/>,
    /// tracking nesting to find the matching close.
    /// </summary>
    public PatternBuilder<TKind> Balanced(TKind open, TKind close) {
        _elements.Add(new Balanced<TKind>(open, close));
        return this;
    }

    /// <summary>Appends a wildcard that matches any single token.</summary>
    public PatternBuilder<TKind> Any() {
        _elements.Add(new AnyToken<TKind>());
        return this;
    }

    /// <summary>Commits the pattern to the grammar and returns the parent rule builder.</summary>
    public RuleBuilder<TKind> Commit() {
        _grammar.AddPattern(_ruleName, new Pattern<TKind>(_name, _elements.ToArray()));
        return _parent;
    }
}