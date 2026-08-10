namespace Poly.Grammar;

/// <summary>
/// A grammar is a named collection of <see cref="Pattern{TToken,TTokenKind}"/> grouped
/// into rules. Each rule is a context in which a set of patterns are valid; the
/// <see cref="Matcher{TToken,TTokenKind}"/> finds the longest matching pattern at the
/// current position.
///
/// Read-only at match time (patterns are sorted on registration), so concurrent
/// matchers may share one grammar safely.
/// </summary>
public sealed class Grammar<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly Dictionary<string, List<Pattern<TToken, TTokenKind>>> _rules = new();

    /// <summary>Begins defining patterns in a named rule group.</summary>
    public RuleBuilder<TToken, TTokenKind> Define(string ruleName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        return new RuleBuilder<TToken, TTokenKind>(this, ruleName);
    }

    /// <summary>
    /// Returns all patterns registered under <paramref name="ruleName"/>, sorted by
    /// first-token kind then element count descending. Lenient for unknown rules
    /// (empty list) — the matcher validates rule names before matching (N3) so
    /// <see cref="Repeat{TToken,TTokenKind}"/> keeps zero-many-on-unknown semantics
    /// while typo'd rule references fail at the source.
    /// </summary>
    public IReadOnlyList<Pattern<TToken, TTokenKind>> GetPatterns(string ruleName) =>
        _rules.TryGetValue(ruleName, out var list) ? list : [];

    /// <summary>True when a rule with this name is defined.</summary>
    public bool HasRule(string ruleName) => _rules.ContainsKey(ruleName);

    /// <summary>All known rule names.</summary>
    public IEnumerable<string> KnownRules => _rules.Keys;

    internal void AddPattern(string ruleName, Pattern<TToken, TTokenKind> pattern) {
        if (!_rules.TryGetValue(ruleName, out var list))
            _rules[ruleName] = list = [];
        list.Add(pattern);
        SortPatterns(list);
    }

    /// <summary>
    /// Sorts so that for each distinct first-token kind the longest pattern comes
    /// first; non-kind-led patterns sort last. This makes Repeat safe — the first
    /// match within a token group is always the longest potential match.
    /// </summary>
    private void SortPatterns(List<Pattern<TToken, TTokenKind>> patterns) {
        patterns.Sort((a, b) => {
            var aIsKind = a.Elements.Count > 0 && a.Elements[0] is MatchKind<TToken, TTokenKind>;
            var bIsKind = b.Elements.Count > 0 && b.Elements[0] is MatchKind<TToken, TTokenKind>;

            if (aIsKind != bIsKind)
                return bIsKind.CompareTo(aIsKind);

            // Within same first-kind group, sort by kind (Comparer works for enums,
            // mirroring v1) then element count descending (longest first).
            if (aIsKind && bIsKind) {
                var aKind = ((MatchKind<TToken, TTokenKind>)a.Elements[0]).Kind;
                var bKind = ((MatchKind<TToken, TTokenKind>)b.Elements[0]).Kind;
                var cmp = Comparer<TTokenKind>.Default.Compare(aKind, bKind);
                if (cmp != 0) return cmp;
            }

            return b.Elements.Count.CompareTo(a.Elements.Count);
        });
    }
}

/// <summary>Fluent rule group builder.</summary>
public sealed class RuleBuilder<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly Grammar<TToken, TTokenKind> _grammar;
    private readonly string _ruleName;

    internal RuleBuilder(Grammar<TToken, TTokenKind> grammar, string ruleName) {
        _grammar = grammar;
        _ruleName = ruleName;
    }

    public PatternBuilder<TToken, TTokenKind> Pattern(string name) =>
        new(_grammar, _ruleName, name);
}

/// <summary>Fluent pattern builder — element-by-element, then Commit registers the pattern.</summary>
public sealed class PatternBuilder<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly Grammar<TToken, TTokenKind> _grammar;
    private readonly string _ruleName;
    private readonly string _name;
    private readonly List<IPatternElement<TToken, TTokenKind>> _elements = [];

    internal PatternBuilder(Grammar<TToken, TTokenKind> grammar, string ruleName, string name) {
        _grammar = grammar;
        _ruleName = ruleName;
        _name = name;
    }

    public PatternBuilder<TToken, TTokenKind> Kind(TTokenKind kind) {
        _elements.Add(new MatchKind<TToken, TTokenKind>(kind));
        return this;
    }

    public PatternBuilder<TToken, TTokenKind> Value(TTokenKind kind) {
        _elements.Add(new Value<TToken, TTokenKind>(kind));
        return this;
    }

    public PatternBuilder<TToken, TTokenKind> Predicate(Func<TToken, bool> predicate, string label) {
        _elements.Add(new MatchPredicate<TToken, TTokenKind>(predicate, label));
        return this;
    }

    public PatternBuilder<TToken, TTokenKind> Optional(IPatternElement<TToken, TTokenKind> inner) {
        _elements.Add(new Optional<TToken, TTokenKind>(inner));
        return this;
    }

    /// <summary>Convenience: optional single token of a kind (wraps <see cref="MatchKind{TToken,TTokenKind}"/>).</summary>
    public PatternBuilder<TToken, TTokenKind> Optional(TTokenKind kind) =>
        Optional(new MatchKind<TToken, TTokenKind>(kind));

    public PatternBuilder<TToken, TTokenKind> Repeat(string ruleName, int min = 0, int max = int.MaxValue) {
        _elements.Add(new Repeat<TToken, TTokenKind>(ruleName, min, max));
        return this;
    }

    public PatternBuilder<TToken, TTokenKind> Ref(string ruleName) {
        _elements.Add(new Ref<TToken, TTokenKind>(ruleName));
        return this;
    }

    public PatternBuilder<TToken, TTokenKind> LeftAssoc(string operandRule, params TTokenKind[] operatorKinds) {
        _elements.Add(new LeftAssoc<TToken, TTokenKind>(operandRule, operatorKinds));
        return this;
    }

    public PatternBuilder<TToken, TTokenKind> Balanced(TTokenKind open, TTokenKind close) {
        _elements.Add(new Balanced<TToken, TTokenKind>(open, close));
        return this;
    }

    public PatternBuilder<TToken, TTokenKind> Any() {
        _elements.Add(new Any<TToken, TTokenKind>());
        return this;
    }

    /// <summary>Commits this pattern and returns the rule builder for the next pattern in the rule.</summary>
    public RuleBuilder<TToken, TTokenKind> Commit() {
        _grammar.AddPattern(_ruleName, new Pattern<TToken, TTokenKind>(_name, _elements));
        return new RuleBuilder<TToken, TTokenKind>(_grammar, _ruleName);
    }
}