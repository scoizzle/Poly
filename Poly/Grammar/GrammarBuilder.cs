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
    private int _nextOrder;

    public GrammarBuilder() {
        _rules = new Dictionary<string, List<Pattern<TToken, TTokenKind>>>(StringComparer.Ordinal);
    }

    private GrammarBuilder(Dictionary<string, List<Pattern<TToken, TTokenKind>>> rules) {
        _rules = rules;
    }

    internal static GrammarBuilder<TToken, TTokenKind> From(Grammar<TToken, TTokenKind> grammar) {
        ArgumentNullException.ThrowIfNull(grammar);
        var rules = new Dictionary<string, List<Pattern<TToken, TTokenKind>>>(StringComparer.Ordinal);
        var nextOrder = 0;
        foreach (var (rule, patterns) in grammar.Rules) {
            rules[rule] = [.. patterns];
            foreach (var pattern in patterns)
                nextOrder = Math.Max(nextOrder, pattern.Order + 1);
        }
        return new GrammarBuilder<TToken, TTokenKind>(rules) { _nextOrder = nextOrder };
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
        EnsureEnclosingCaptures(frozen);
        return new Grammar<TToken, TTokenKind>(frozen);
    }

    /// <summary>
    /// A template placeholder that is not the row's own capture names an enclosing row's: every row that reaches the rule
    /// (a top-level or optional Ref, Repeat or LeftAssoc operand) must capture it with a required element before that
    /// reference, or be reached only by rows that do. A rule nothing reaches has no enclosing row, so the name fails.
    /// </summary>
    private static void EnsureEnclosingCaptures(Dictionary<string, List<Pattern<TToken, TTokenKind>>> rules) {
        foreach (var (rule, patterns) in rules) {
            foreach (var pattern in patterns) {
                var own = CaptureNames(pattern.Elements).ToHashSet(StringComparer.Ordinal);
                foreach (var element in pattern.Elements) {
                    var template = element switch {
                        Refuse<TToken, TTokenKind> refuse => refuse.Message,
                        IExpectedElement { ExpectTemplate: { } t } => t,
                        Optional<TToken, TTokenKind> { Inner: IExpectedElement { ExpectTemplate: { } t } } => t,
                        _ => null,
                    };
                    foreach (var name in template is null ? [] : MatchFailure<TToken, TTokenKind>.PlaceholdersOf(template)) {
                        if (name is not ("expected" or "got" or "token") && !own.Contains(name) && !Enclosed(rules, rule, name, [rule]))
                            throw new InvalidOperationException(
                                $"The template of '{rule}/{pattern.Name}' names '{{{name}}}', which neither the row nor every row that reaches '{rule}' captures before it.");
                    }
                }
            }
        }
    }

    private static bool Enclosed(Dictionary<string, List<Pattern<TToken, TTokenKind>>> rules, string rule, string name, HashSet<string> visited) {
        var reached = false;
        foreach (var (outer, patterns) in rules) {
            foreach (var pattern in patterns) {
                var before = new HashSet<string>(StringComparer.Ordinal);
                foreach (var element in pattern.Elements) {
                    var target = element switch {
                        Ref<TToken, TTokenKind> r => r.RuleName,
                        Repeat<TToken, TTokenKind> r => r.RuleName,
                        LeftAssoc<TToken, TTokenKind> l => l.OperandRule,
                        Optional<TToken, TTokenKind> { Inner: Ref<TToken, TTokenKind> r } => r.RuleName,
                        _ => null,
                    };
                    if (target == rule) {
                        reached = true;
                        if (!before.Contains(name) && (visited.Contains(outer) || !Enclosed(rules, outer, name, [.. visited, outer])))
                            return false;
                    }
                    if (element is not (Optional<TToken, TTokenKind> or NotFollowedBy<TToken, TTokenKind>))
                        before.UnionWith(CaptureNames([element]));
                }
            }
        }
        return reached;
    }

    /// <summary>
    /// Binds handler meaning onto an existing pattern. Unknown rule/pattern fails closed.
    /// </summary>
    public GrammarBuilder<TToken, TTokenKind> AttachPayload(string ruleName, string patternName, object payload) {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(patternName);
        ArgumentNullException.ThrowIfNull(payload);
        if (!_rules.TryGetValue(ruleName, out var list))
            throw new InvalidOperationException($"Unknown rule '{ruleName}'.");
        for (var i = 0; i < list.Count; i++) {
            if (string.Equals(list[i].Name, patternName, StringComparison.Ordinal)) {
                list[i] = list[i].WithPayload(payload);
                return this;
            }
        }
        throw new InvalidOperationException($"Unknown pattern '{patternName}' in rule '{ruleName}'.");
    }

    /// <summary>The next declaration order (<see cref="Pattern{TToken,TTokenKind}.Order"/>).</summary>
    internal int NextOrder() => _nextOrder++;

    internal void AddPattern(string ruleName, Pattern<TToken, TTokenKind> pattern) {
        if (!_rules.TryGetValue(ruleName, out var list))
            _rules[ruleName] = list = [];
        if (list.Any(p => string.Equals(p.Name, pattern.Name, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Pattern '{pattern.Name}' is already defined on rule '{ruleName}'.");
        EnsurePlacement(ruleName, pattern);
        EnsureUniqueCaptureNames(ruleName, pattern);
        list.Add(pattern);
        SortPatterns(list);
    }

    /// <summary>
    /// Fails closed on an element where it cannot work: a <see cref="Slot{TToken,TTokenKind}"/> that is not
    /// the row's last element or has no required element before it (a Slot-only row matches zero tokens
    /// anywhere); a <see cref="Run{TToken,TTokenKind}"/> with no required element before it (it continues the
    /// token before it); a Slot or Run inside an <see cref="Optional{TToken,TTokenKind}"/>; a
    /// <see cref="Cut{TToken,TTokenKind}"/> with no required element before it, last, inside an Optional, or a
    /// second one; a <see cref="Refuse{TToken,TTokenKind}"/> that is not last, follows only optional elements (a
    /// Refuse-only row is a catch-all that matches no token), or is inside an Optional; and a template placeholder that is not <c>{expected}</c>, <c>{got}</c> or the capture
    /// of a required element before it.
    /// </summary>
    private static void EnsurePlacement(string ruleName, Pattern<TToken, TTokenKind> pattern) {
        var elements = pattern.Elements;
        var required = false;
        var cuts = 0;
        var captured = new HashSet<string>(StringComparer.Ordinal);
        string Where(int i) => $"element {i + 1} of '{ruleName}/{pattern.Name}'";
        for (var i = 0; i < elements.Count; i++) {
            switch (elements[i]) {
                case Run<TToken, TTokenKind> run when !required:
                    throw new InvalidOperationException(
                        $"Run '{run.Name}' on '{ruleName}/{pattern.Name}' is element {i + 1} with no required element before it; a Run continues the token before it.");
                case Optional<TToken, TTokenKind> { Inner: Run<TToken, TTokenKind> inner }:
                    throw new InvalidOperationException(
                        $"Run '{inner.Name}' on '{ruleName}/{pattern.Name}' is inside an Optional; a Run is never optional.");
                case Slot<TToken, TTokenKind> slot when i != elements.Count - 1:
                    throw new InvalidOperationException(
                        $"Slot '{slot.Name}' on '{ruleName}/{pattern.Name}' is element {i + 1} of {elements.Count}; a Slot must be the last element.");
                case Slot<TToken, TTokenKind> slot when !required:
                    throw new InvalidOperationException(
                        $"Slot '{slot.Name}' on '{ruleName}/{pattern.Name}' has no required element before it; a row of only a Slot matches zero tokens anywhere.");
                case Optional<TToken, TTokenKind> { Inner: Slot<TToken, TTokenKind> inner }:
                    throw new InvalidOperationException(
                        $"Slot '{inner.Name}' on '{ruleName}/{pattern.Name}' is inside an Optional; a Slot is never optional.");
                case Cut<TToken, TTokenKind> when !required:
                    throw new InvalidOperationException($"The Cut is {Where(i)} with no required element before it; a Cut commits a row once its head has matched.");
                case Cut<TToken, TTokenKind> when i == elements.Count - 1:
                    throw new InvalidOperationException($"The Cut is the last element of '{ruleName}/{pattern.Name}'; a Cut with nothing after it commits nothing.");
                case Cut<TToken, TTokenKind> when ++cuts > 1:
                    throw new InvalidOperationException($"'{ruleName}/{pattern.Name}' holds more than one Cut; a row commits once.");
                case Refuse<TToken, TTokenKind> when i != elements.Count - 1:
                    throw new InvalidOperationException($"The Refuse is {Where(i)} of {elements.Count}; a Refuse must be the last element.");
                case Refuse<TToken, TTokenKind> when !required && i != 0:
                    throw new InvalidOperationException($"The Refuse is {Where(i)} after only optional elements; a refusal stands on a matched token, or is the whole row (a catch-all).");
                case Refuse<TToken, TTokenKind> { At: { } at } when Refuse<TToken, TTokenKind>.AnchorOf(elements, at) is var anchor && (anchor < 0 || anchor >= i):
                    throw new InvalidOperationException($"The Refuse of '{ruleName}/{pattern.Name}' stands at '{at}', which names no required capture or Word before it.");
                case Optional<TToken, TTokenKind> { Inner: Cut<TToken, TTokenKind> or Refuse<TToken, TTokenKind> }:
                    throw new InvalidOperationException($"{Where(i)} is an Optional Cut or Refuse; neither is ever optional.");
            }
            var template = elements[i] switch {
                Refuse<TToken, TTokenKind> refuse => refuse.Message,
                IExpectedElement { ExpectTemplate: { } t } => t,
                Optional<TToken, TTokenKind> { Inner: IExpectedElement { ExpectTemplate: { } t } } => t,
                _ => null,
            };
            if (template is not null) {
                // A name the row does not capture at all may be an enclosing row's: Build checks that (EnsureEnclosingCaptures).
                var own = CaptureNames(elements).ToHashSet(StringComparer.Ordinal);
                foreach (var name in MatchFailure<TToken, TTokenKind>.PlaceholdersOf(template)) {
                    if (name is not ("expected" or "got" or "token") && !captured.Contains(name) && own.Contains(name))
                        throw new InvalidOperationException(
                            $"The template of {Where(i)} names '{{{name}}}', which the row captures only after it or optionally.");
                }
            }
            if (elements[i] is not (Optional<TToken, TTokenKind> or NotFollowedBy<TToken, TTokenKind>
                or Cut<TToken, TTokenKind> or Refuse<TToken, TTokenKind> or Slot<TToken, TTokenKind>)) {
                required = true;
                foreach (var name in CaptureNames([elements[i]]))
                    captured.Add(name);
            }
        }
    }

    private static void EnsureUniqueCaptureNames(string ruleName, Pattern<TToken, TTokenKind> pattern) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // A Slot's name is a print fill key, so it shares the namespace of the captures.
        var slots = pattern.Elements.OfType<Slot<TToken, TTokenKind>>().Select(slot => slot.Name);
        foreach (var name in CaptureNames(pattern.Elements).Concat(slots)) {
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
                case Run<TToken, TTokenKind> run:
                    yield return run.Name;
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
    private object? _payload;

    internal PatternBuilder(GrammarBuilder<TToken, TTokenKind> builder, string ruleName, string name, int priority = 0) {
        _builder = builder;
        _ruleName = ruleName;
        _name = name;
        _priority = priority;
    }

    /// <summary>Matches any token of the specified <paramref name="kind"/>.</summary>
    public PatternBuilder<TToken, TTokenKind> Kind(TTokenKind kind) {
        _elements.Add(new MatchKind<TToken, TTokenKind>(kind));
        return this;
    }

    /// <summary>Matches a token of the specified <paramref name="kind"/> and captures it as <paramref name="name"/>.</summary>
    public PatternBuilder<TToken, TTokenKind> Value(TTokenKind kind, string? name = null) {
        _elements.Add(new Value<TToken, TTokenKind>(kind, name));
        return this;
    }

    /// <summary>Matches a token satisfying <paramref name="predicate"/> and captures the result under <paramref name="label"/>.</summary>
    public PatternBuilder<TToken, TTokenKind> Predicate(Func<TToken, bool> predicate, string label) {
        _elements.Add(new MatchPredicate<TToken, TTokenKind>(predicate, label));
        return this;
    }

    /// <summary>
    /// Matches one token <paramref name="matches"/> accepts (a contextual word); prints <paramref name="text"/>
    /// as a token of <paramref name="kind"/>. Captures nothing.
    /// </summary>
    public PatternBuilder<TToken, TTokenKind> Word(TTokenKind kind, string text, Func<TToken, bool> matches) {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNull(matches);
        _elements.Add(new Word<TToken, TTokenKind>(kind, text, matches));
        return this;
    }

    /// <summary>
    /// Ends the row at a zero-width print hole named <paramref name="name"/>: the reader stops here, the
    /// printer writes the fill. Must be the last element.
    /// </summary>
    public PatternBuilder<TToken, TTokenKind> Slot(string name) {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _elements.Add(new Slot<TToken, TTokenKind>(name));
        return this;
    }

    /// <summary>
    /// Matches one or more tokens, each accepted by <paramref name="continues"/> <c>(previous, candidate, index)</c>,
    /// captured and printed as <paramref name="name"/>. Not the first element.
    /// </summary>
    public PatternBuilder<TToken, TTokenKind> Run(Func<TToken, TToken, int, bool> continues, string name) {
        ArgumentNullException.ThrowIfNull(continues);
        ArgumentException.ThrowIfNullOrEmpty(name);
        _elements.Add(new Run<TToken, TTokenKind>(continues, name));
        return this;
    }

    /// <summary>
    /// Names the element before it in a failure report: <paramref name="label"/> is what the report says was
    /// expected, and <paramref name="template"/> (optional) is the row's own message when this element is the first
    /// expected element (placeholders <c>{expected}</c>, <c>{got}</c> and a capture name of a required element
    /// before it). Only a token element takes one: a Kind, Value, Predicate, Word or Run.
    /// </summary>
    public PatternBuilder<TToken, TTokenKind> Expect(string label, string? template = null) {
        ArgumentException.ThrowIfNullOrEmpty(label);
        if (_elements.Count == 0)
            throw new InvalidOperationException($"Expect names the element before it; pattern '{_name}' in rule '{_ruleName}' has none.");
        _elements[^1] = _elements[^1] switch {
            MatchKind<TToken, TTokenKind> e => e with { Expect = label, ExpectTemplate = template },
            Value<TToken, TTokenKind> e => e with { Expect = label, ExpectTemplate = template },
            MatchPredicate<TToken, TTokenKind> e => e with { Expect = label, ExpectTemplate = template },
            Word<TToken, TTokenKind> e => e with { Expect = label, ExpectTemplate = template },
            Run<TToken, TTokenKind> e => e with { Expect = label, ExpectTemplate = template },
            var other => throw new InvalidOperationException(
                $"Expect names a token element (Kind, Value, Predicate, Word or Run); element {_elements.Count} of pattern '{_name}' in rule '{_ruleName}' is a {other.GetType().Name[..other.GetType().Name.IndexOf('`')]}."),
        };
        return this;
    }

    /// <summary>
    /// Commits the row once the elements before it have matched (<see cref="Cut{TToken,TTokenKind}"/>). The convention,
    /// enforced at <c>Commit</c>: the Cut goes after the row's head tokens (at least one required element before it), never
    /// first and never last, and a row holds one; a rule with a Cut is matched with <c>Match</c>.
    /// </summary>
    public PatternBuilder<TToken, TTokenKind> Cut() {
        _elements.Add(new Cut<TToken, TTokenKind>());
        return this;
    }

    /// <summary>Ends the row with a refusal: reaching it yields a committed failure with <paramref name="message"/> (<see cref="Refuse{TToken,TTokenKind}"/>).</summary>
    public PatternBuilder<TToken, TTokenKind> Refuse(string message, string? at = null) {
        ArgumentException.ThrowIfNullOrEmpty(message);
        _elements.Add(new Refuse<TToken, TTokenKind>(message, at));
        return this;
    }

    /// <summary>Makes <paramref name="inner"/> optional — matches zero or one occurrence.</summary>
    public PatternBuilder<TToken, TTokenKind> Optional(IPatternElement<TToken, TTokenKind> inner) {
        _elements.Add(new Optional<TToken, TTokenKind>(inner));
        return this;
    }

    /// <summary>Convenience: optional single token of a kind (wraps <see cref="MatchKind{TToken,TTokenKind}"/>).</summary>
    public PatternBuilder<TToken, TTokenKind> Optional(TTokenKind kind) =>
        Optional(new MatchKind<TToken, TTokenKind>(kind));

    /// <summary>Repeats a reference to <paramref name="ruleName"/> between <paramref name="min"/> and <paramref name="max"/> times.</summary>
    public PatternBuilder<TToken, TTokenKind> Repeat(string ruleName, int min = 0, int max = int.MaxValue) {
        _elements.Add(new Repeat<TToken, TTokenKind>(ruleName, min, max));
        return this;
    }

    /// <summary>References another rule by <paramref name="ruleName"/>.</summary>
    public PatternBuilder<TToken, TTokenKind> Ref(string ruleName) {
        _elements.Add(new Ref<TToken, TTokenKind>(ruleName));
        return this;
    }

    /// <summary>Matches a left-associative binary expression: <c>operand operator operand</c>.</summary>
    public PatternBuilder<TToken, TTokenKind> LeftAssoc(string operandRule, params TTokenKind[] operatorKinds) {
        _elements.Add(new LeftAssoc<TToken, TTokenKind>(operandRule, operatorKinds));
        return this;
    }

    /// <summary>Matches a balanced pair of <paramref name="open"/> and <paramref name="close"/> delimiters.</summary>
    public PatternBuilder<TToken, TTokenKind> Balanced(TTokenKind open, TTokenKind close) {
        _elements.Add(new Balanced<TToken, TTokenKind>(open, close));
        return this;
    }

    /// <summary>Matches any single token.</summary>
    public PatternBuilder<TToken, TTokenKind> Any() {
        _elements.Add(new Any<TToken, TTokenKind>());
        return this;
    }

    /// <summary>Asserts the pattern is not followed by a token of <paramref name="kind"/>.</summary>
    public PatternBuilder<TToken, TTokenKind> NotFollowedBy(TTokenKind kind) {
        _elements.Add(new NotFollowedBy<TToken, TTokenKind>(kind));
        return this;
    }

    /// <summary>Handler-owned meaning. The engine does not interpret this.</summary>
    public PatternBuilder<TToken, TTokenKind> Payload(object payload) {
        ArgumentNullException.ThrowIfNull(payload);
        _payload = payload;
        return this;
    }

    /// <summary>Registers this pattern on the builder; ready for the next pattern in the same rule.</summary>
    public RuleBuilder<TToken, TTokenKind> Commit() {
        _builder.AddPattern(_ruleName, new Pattern<TToken, TTokenKind>(_name, _elements, _priority, _payload) { Order = _builder.NextOrder() });
        return new RuleBuilder<TToken, TTokenKind>(_builder, _ruleName);
    }
}