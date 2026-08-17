namespace Poly.Grammar;

/// <summary>
/// Mutable construction of a <see cref="Grammar{TToken,TTokenKind}"/>.
/// <see cref="Build"/> freezes one table. Use this for product/core tables and
/// library contributions so <c>Commit</c> does not allocate an intermediate grammar.
/// </summary>
public sealed class GrammarBuilder<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly Dictionary<string, List<Pattern<TToken, TTokenKind>>> _rules;

    public GrammarBuilder() {
        _rules = new Dictionary<string, List<Pattern<TToken, TTokenKind>>>(StringComparer.Ordinal);
    }

    private GrammarBuilder(Dictionary<string, List<Pattern<TToken, TTokenKind>>> rules) {
        _rules = rules;
    }

    internal static GrammarBuilder<TToken, TTokenKind> From(Grammar<TToken, TTokenKind> grammar) {
        ArgumentNullException.ThrowIfNull(grammar);
        var rules = new Dictionary<string, List<Pattern<TToken, TTokenKind>>>(StringComparer.Ordinal);
        foreach (var (rule, patterns) in grammar.Rules)
            rules[rule] = [.. patterns];
        return new GrammarBuilder<TToken, TTokenKind>(rules);
    }

    /// <summary>Begins defining patterns in a named rule group.</summary>
    public RuleBuilder<TToken, TTokenKind> Define(string ruleName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        return new RuleBuilder<TToken, TTokenKind>(this, ruleName);
    }

    /// <summary>Frozen snapshot. Further <see cref="Define"/> on this builder is allowed and is not reflected in the snapshot.</summary>
    public Grammar<TToken, TTokenKind> Build() {
        var frozen = new Dictionary<string, List<Pattern<TToken, TTokenKind>>>(StringComparer.Ordinal);
        foreach (var (rule, patterns) in _rules)
            frozen[rule] = [.. patterns];
        return new Grammar<TToken, TTokenKind>(frozen);
    }

    internal void AddPattern(string ruleName, Pattern<TToken, TTokenKind> pattern) {
        if (!_rules.TryGetValue(ruleName, out var list))
            _rules[ruleName] = list = [];
        if (list.Any(p => string.Equals(p.Name, pattern.Name, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Pattern '{pattern.Name}' is already defined on rule '{ruleName}'.");
        EnsureUniqueCaptureNames(ruleName, pattern);
        list.Add(pattern);
        SortPatterns(list);
    }

    private static void EnsureUniqueCaptureNames(string ruleName, Pattern<TToken, TTokenKind> pattern) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in CaptureNames(pattern.Elements)) {
            if (!seen.Add(name))
                throw new InvalidOperationException(
                    $"Duplicate capture '{name}' on pattern '{pattern.Name}' in rule '{ruleName}'.");
        }
    }

    internal static IEnumerable<string> CaptureNames(IEnumerable<IPatternElement<TToken, TTokenKind>> elements) {
        foreach (var element in elements) {
            switch (element) {
                case Value<TToken, TTokenKind> { Name: { Length: > 0 } name }:
                    yield return name;
                    break;
                case MatchPredicate<TToken, TTokenKind> { Label: { Length: > 0 } label }:
                    yield return label;
                    break;
                case Optional<TToken, TTokenKind> opt:
                    foreach (var inner in CaptureNames([opt.Inner]))
                        yield return inner;
                    break;
            }
        }
    }

    internal static void SortPatterns(List<Pattern<TToken, TTokenKind>> patterns) {
        patterns.Sort((a, b) => {
            var aIsKind = a.Elements.Count > 0 && a.Elements[0] is MatchKind<TToken, TTokenKind>;
            var bIsKind = b.Elements.Count > 0 && b.Elements[0] is MatchKind<TToken, TTokenKind>;

            if (aIsKind != bIsKind)
                return bIsKind.CompareTo(aIsKind);

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

/// <summary>Fluent rule group on a <see cref="GrammarBuilder{TToken,TTokenKind}"/>.</summary>
public sealed class RuleBuilder<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly GrammarBuilder<TToken, TTokenKind> _builder;
    private readonly string _ruleName;

    internal RuleBuilder(GrammarBuilder<TToken, TTokenKind> builder, string ruleName) {
        _builder = builder;
        _ruleName = ruleName;
    }

    public PatternBuilder<TToken, TTokenKind> Pattern(string name, int priority = 0) =>
        new(_builder, _ruleName, name, priority);

    public RuleBuilder<TToken, TTokenKind> Define(string ruleName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        return new RuleBuilder<TToken, TTokenKind>(_builder, ruleName);
    }

    /// <summary>Freezes the builder's current table.</summary>
    public Grammar<TToken, TTokenKind> Build() => _builder.Build();

    public static implicit operator Grammar<TToken, TTokenKind>(RuleBuilder<TToken, TTokenKind> builder) =>
        builder.Build();
}

/// <summary>Fluent pattern builder. <see cref="Commit"/> mutates the builder, not a grammar.</summary>
public sealed class PatternBuilder<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly GrammarBuilder<TToken, TTokenKind> _builder;
    private readonly string _ruleName;
    private readonly string _name;
    private readonly int _priority;
    private readonly List<IPatternElement<TToken, TTokenKind>> _elements = [];

    internal PatternBuilder(GrammarBuilder<TToken, TTokenKind> builder, string ruleName, string name, int priority = 0) {
        _builder = builder;
        _ruleName = ruleName;
        _name = name;
        _priority = priority;
    }

    public PatternBuilder<TToken, TTokenKind> Kind(TTokenKind kind) {
        _elements.Add(new MatchKind<TToken, TTokenKind>(kind));
        return this;
    }

    public PatternBuilder<TToken, TTokenKind> Value(TTokenKind kind, string? name = null) {
        _elements.Add(new Value<TToken, TTokenKind>(kind, name));
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

    /// <summary>Registers this pattern on the builder; ready for the next pattern in the same rule.</summary>
    public RuleBuilder<TToken, TTokenKind> Commit() {
        _builder.AddPattern(_ruleName, new Pattern<TToken, TTokenKind>(_name, _elements, _priority));
        return new RuleBuilder<TToken, TTokenKind>(_builder, _ruleName);
    }
}