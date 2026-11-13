namespace Poly.Grammar;

/// <summary>
/// Longest-match pattern scanner over a token stream. Recognition only — the result
/// is a form tree (nested rule matches + leaf captures). Folding into IR is the
/// handler's job.
///
/// Fail-closed by design (learned from v1):
/// - unknown rule name in TryMatch / Ref / LeftAssoc operand → throws (a typo must
///   fail at the source, not silently never-match); Repeat keeps zero-many on
///   unknown rules (documented Repeat semantics)
/// - trailing operator in LeftAssoc → the whole chain fails
/// - zero-width sub-match in Ref / Repeat / LeftAssoc operand → failure (recursion guard)
/// - Repeat below Min → failure; bounded by Max, never a hard magic cap
///
/// Failure report (G-28), on <see cref="Match"/> only: every failing token element records
/// its offset, and the report is the furthest offset with the union of what was expected
/// there. A <see cref="Cut{TToken,TTokenKind}"/> commits its row: a later failure in that
/// row is rescued only by a sibling of the same rule that matches more tokens than the
/// committed row had matched; otherwise it escapes the rule and aborts the call (outer
/// siblings are not tried, and an abort is never memoised). <see cref="TryMatch"/> pays one
/// null check per failing element and throws for a rule that can commit.
/// </summary>
public sealed class Matcher<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    private readonly Grammar<TToken, TTokenKind> _grammar;
    private readonly ITokenReader<TToken, TTokenKind> _reader;

    public Matcher(Grammar<TToken, TTokenKind> grammar, ITokenReader<TToken, TTokenKind> reader) {
        _grammar = grammar ?? throw new ArgumentNullException(nameof(grammar));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public Grammar<TToken, TTokenKind> Grammar => _grammar;

    private MatchResult<TToken, TTokenKind>? TryMatchRuleAt(string ruleName, int offset) {
        EnsureKnownRule(ruleName);
        var key = (ruleName, offset);
        if (_memo is not null && _memo.TryGetValue(key, out var cached))
            return cached;

        MatchResult<TToken, TTokenKind>? best = null;
        var bestPriority = int.MinValue;
        Commit? committed = null;
        foreach (var pattern in _grammar.GetPatterns(ruleName)) {
            if (!TryMatchPattern(ruleName, pattern, offset, out var match, ref committed))
                continue;
            if (best is null
                || match.Consumed > best.Consumed
                || (match.Consumed == best.Consumed && pattern.Priority > bestPriority)) {
                best = match;
                bestPriority = pattern.Priority;
            }
        }
        // A committed row is rescued only by a sibling that matched more tokens than it had; otherwise
        // the commit escapes this rule and aborts the call (it is never memoised).
        if (committed is not null && (best is null || best.Consumed <= committed.Reach - offset))
            throw new CommitAbort(Report(committed.Entries, committed.Reach, committed: true));
        _memo?[key] = best;
        return best;
    }

    /// <summary>
    /// Attempts to match the named rule at the current position. The reader is only
    /// examined, never consumed — callers commit a successful match via
    /// <c>reader.Consume(match.Consumed)</c>.
    ///
    /// True longest-match: every pattern in the rule is tried and the one consuming
    /// the most tokens wins (not merely the first in sorted order). Equal length
    /// prefers higher <see cref="Pattern{TToken,TTokenKind}.Priority"/>; equal
    /// priority keeps the first winner. Nested <see cref="Ref{TToken,TTokenKind}"/>,
    /// LeftAssoc operands, and <see cref="Repeat{TToken,TTokenKind}"/> items use
    /// the same rule.
    ///
    /// Rule/offset results are memoized for the duration of one attempt (a packrat
    /// table): recursive expression grammars retry the same subrule at the same
    /// position while branching, and recognition is pure while the reader position
    /// is unchanged. The table is scoped to this call because offsets are relative
    /// to the reader's committed position.
    /// </summary>
    /// <exception cref="InvalidOperationException">The rule holds a <see cref="Cut{TToken,TTokenKind}"/> or a
    /// <see cref="Refuse{TToken,TTokenKind}"/>, directly or through a rule it references (whether or not this input
    /// would reach it): a commit has no meaning without the report, so such a rule is matched with <see cref="Match"/>.</exception>
    public MatchResult<TToken, TTokenKind>? TryMatch(string ruleName) {
        if (_grammar.CanCommit(ruleName))
            throw new InvalidOperationException(
                $"Rule '{ruleName}' holds a Cut or a Refuse (directly or through a rule it references); match it with Match, which reports the committed failure.");
        var previous = _memo;
        var previousTracker = _tracker;
        _memo = new Dictionary<(string Rule, int Offset), MatchResult<TToken, TTokenKind>?>();
        _tracker = null;
        try {
            return TryMatchRuleAt(ruleName, offset: 0);
        }
        finally {
            _memo = previous;
            _tracker = previousTracker;
        }
    }

    /// <summary>
    /// <see cref="TryMatch"/> with the failure report: the match, or what the table says went wrong. The report is
    /// the furthest failure and the union of the elements expected there, sorted by declaration order; when a row
    /// committed (<see cref="Cut{TToken,TTokenKind}"/> or <see cref="Refuse{TToken,TTokenKind}"/>) and nothing rescued it,
    /// the report comes from the committed failures only. A successful match reports nothing. Each returned failure
    /// is also offered to <see cref="MatchTrace"/> (observation only).
    /// </summary>
    public MatchOutcome<TToken, TTokenKind> Match(string ruleName) {
        EnsureKnownRule(ruleName);
        var previous = _memo;
        var previousTracker = _tracker;
        // The accumulator is created on the first Match of this matcher and reused (a nested Match gets its own).
        var tracker = previousTracker is null ? _spareTracker ??= new FailureTracker() : new FailureTracker();
        tracker.Reset();
        _memo = new Dictionary<(string Rule, int Offset), MatchResult<TToken, TTokenKind>?>();
        _tracker = tracker;
        MatchFailure<TToken, TTokenKind> failure;
        try {
            if (TryMatchRuleAt(ruleName, offset: 0) is { } result)
                return new(result, null);
            failure = Report(tracker.Entries, tracker.Far, committed: false);
        }
        catch (CommitAbort abort) {
            failure = abort.Failure;
        }
        finally {
            _memo = previous;
            _tracker = previousTracker;
        }
        MatchTrace.Record(new MatchObservation(ruleName, failure));
        return new(null, failure);
    }

    private Dictionary<(string Rule, int Offset), MatchResult<TToken, TTokenKind>?>? _memo;

    // Failure tracking: non-null only inside Match. _frame names the row element being matched, for the record.
    private FailureTracker? _tracker;
    private FailureTracker? _spareTracker;
    private Frame _frame;

    private void EnsureKnownRule(string ruleName) {
        if (!_grammar.HasRule(ruleName))
            throw new ArgumentException($"Unknown grammar rule '{ruleName}'", nameof(ruleName));
    }

    /// <summary>
    /// Returns the distinct first-token kinds that can start a pattern in the
    /// named rule. Used for error messages and introspection. Only patterns whose
    /// first element is a concrete <see cref="MatchKind{TToken,TTokenKind}"/> or
    /// <see cref="Value{TToken,TTokenKind}"/> are included; predicate-led patterns
    /// contribute through runtime evaluation.
    /// </summary>
    public IEnumerable<TTokenKind> ExpectedTokens(string ruleName) {
        var seen = new HashSet<TTokenKind>();
        foreach (var pattern in _grammar.GetPatterns(ruleName)) {
            if (pattern.Elements.Count == 0) continue;
            switch (pattern.Elements[0]) {
                case MatchKind<TToken, TTokenKind> mk:
                    seen.Add(mk.Kind);
                    break;
                case Value<TToken, TTokenKind> mv:
                    seen.Add(mv.Kind);
                    break;
            }
        }
        return seen;
    }

    private bool TryMatchPattern(
        string ruleName,
        Pattern<TToken, TTokenKind> pattern,
        int offset,
        out MatchResult<TToken, TTokenKind> match,
        ref Commit? committed) {
        var tokens = new List<TToken>();
        var children = new List<MatchResult<TToken, TTokenKind>>();
        var operators = new List<TToken>();
        var captures = new Dictionary<string, IReadOnlyList<TToken>>(StringComparer.Ordinal);
        var pos = offset;
        var cut = false;
        var elements = pattern.Elements;
        for (var index = 0; index < elements.Count; index++) {
            var element = elements[index];
            if (_tracker is not null)
                _frame = new Frame(ruleName, pattern, index, captures);
            switch (element) {
                case Cut<TToken, TTokenKind>:
                    cut = true;
                    continue;
                case Refuse<TToken, TTokenKind>:
                    // The row matched every token up to here and refuses at the last one.
                    Commit.Add(ref committed, new Commit(pos, [new Entry(ruleName, pattern, index, element, pos - 1, null, captures)]));
                    match = null!;
                    return false;
            }
            if (!TryMatchElement(element, pos, captures, tokens, out var piece)) {
                if (cut)
                    Commit.Add(ref committed, CommitAt(new Frame(ruleName, pattern, index, captures), element, pos, tokens));
                match = null!;
                return false;
            }
            tokens.AddRange(piece.Tokens);
            children.AddRange(piece.Children);
            operators.AddRange(piece.Operators);
            pos += piece.Tokens.Count;
        }
        match = new MatchResult<TToken, TTokenKind>(
            pattern.Name, tokens, captures, ruleName, children, operators, pattern);
        return true;
    }

    private void RecordCapture(
        Dictionary<string, IReadOnlyList<TToken>> captures,
        string? name,
        IReadOnlyList<TToken> tokens) {
        if (string.IsNullOrEmpty(name) || tokens.Count == 0)
            return;
        captures[name] = tokens;
    }

    /// <param name="matched">The tokens this pattern has matched before <paramref name="element"/> (a Run's first previous token).</param>
    private bool TryMatchElement(
        IPatternElement<TToken, TTokenKind> element,
        int offset,
        Dictionary<string, IReadOnlyList<TToken>> captures,
        IReadOnlyList<TToken> matched,
        out Piece piece) {
        switch (element) {
            case MatchKind<TToken, TTokenKind> k:
                if (!TryMatchKind(k.Kind, offset, out var kindTokens)) {
                    Fail(k, offset, k.Kind);
                    piece = default;
                    return false;
                }
                piece = Piece.TokensOnly(kindTokens);
                return true;

            case Value<TToken, TTokenKind> v:
                if (!TryMatchKind(v.Kind, offset, out var valueTokens)) {
                    Fail(v, offset, v.Kind);
                    piece = default;
                    return false;
                }
                RecordCapture(captures, v.Name, valueTokens);
                piece = Piece.TokensOnly(valueTokens);
                return true;

            case MatchPredicate<TToken, TTokenKind> p:
                var pt = _reader.Peek(offset);
                if (!p.Predicate(pt)) {
                    Fail(p, offset, null);
                    piece = default;
                    return false;
                }
                List<TToken> predTokens = [pt];
                RecordCapture(captures, p.Label, predTokens);
                piece = Piece.TokensOnly(predTokens);
                return true;

            case Optional<TToken, TTokenKind> o:
                if (TryMatchElement(o.Inner, offset, captures, matched, out piece))
                    return true;
                piece = Piece.Empty;
                return true;

            case Repeat<TToken, TTokenKind> r:
                return TryRepeat(r, offset, out piece);

            case Ref<TToken, TTokenKind> rr:
                var inner = TryMatchRuleAt(rr.RuleName, offset);
                if (inner is null || inner.Consumed == 0) {
                    piece = default;
                    return false;
                }
                piece = Piece.Child(inner);
                return true;

            case LeftAssoc<TToken, TTokenKind> la:
                return TryLeftAssoc(la, offset, out piece);

            case Balanced<TToken, TTokenKind> b:
                if (!TryBalanced(b, offset, out var balancedTokens)) {
                    piece = default;
                    return false;
                }
                piece = Piece.TokensOnly(balancedTokens);
                return true;

            case Any<TToken, TTokenKind>:
                var at = _reader.Peek(offset);
                if (_reader.EndOfStream(at.Kind)) {
                    Fail(element, offset, null);
                    piece = default;
                    return false;
                }
                piece = Piece.TokensOnly([at]);
                return true;

            case NotFollowedBy<TToken, TTokenKind> n:
                var next = _reader.Peek(offset);
                if (EqualityComparer<TTokenKind>.Default.Equals(next.Kind, n.Kind)) {
                    Fail(n, offset, null);
                    piece = default;
                    return false;
                }
                piece = Piece.Empty;
                return true;

            case Word<TToken, TTokenKind> w:
                var wt = _reader.Peek(offset);
                if (_reader.EndOfStream(wt.Kind) || !w.Matches(wt)) {
                    Fail(w, offset, null);
                    piece = default;
                    return false;
                }
                piece = Piece.TokensOnly([wt]);
                return true;

            case Slot<TToken, TTokenKind>:
                // Zero width: the row ends here and the host reads what follows.
                piece = Piece.Empty;
                return true;

            case Run<TToken, TTokenKind> run:
                if (!TryRun(run, offset, matched, out var runTokens)) {
                    Fail(run, offset, null);
                    piece = default;
                    return false;
                }
                RecordCapture(captures, run.Name, runTokens);
                piece = Piece.TokensOnly(runTokens);
                return true;

            case Cut<TToken, TTokenKind>:
            case Refuse<TToken, TTokenKind>:
                // The builder keeps both at a row's top level, where TryMatchPattern handles them.
                throw new InvalidOperationException($"'{element.GetType().Name}' is matched only as a row element, never nested.");

            default:
                throw new InvalidOperationException($"Unknown pattern element '{element.GetType().Name}'");
        }
    }

    private bool TryMatchKind(TTokenKind kind, int offset, out List<TToken> consumed) {
        var t = _reader.Peek(offset);
        if (!EqualityComparer<TTokenKind>.Default.Equals(t.Kind, kind)) {
            consumed = [];
            return false;
        }
        consumed = [t];
        return true;
    }

    /// <summary>Takes tokens while each continues the one before it; the first's previous token is the last one matched.</summary>
    private bool TryRun(Run<TToken, TTokenKind> run, int offset, IReadOnlyList<TToken> matched, out List<TToken> consumed) {
        consumed = [];
        if (matched.Count == 0)
            return false;
        var previous = matched[^1];
        for (var index = 0; ; index++) {
            var candidate = _reader.Peek(offset + index);
            if (_reader.EndOfStream(candidate.Kind) || !run.Continues(previous, candidate, index))
                break;
            consumed.Add(candidate);
            previous = candidate;
        }
        return consumed.Count > 0;
    }

    private bool TryRepeat(Repeat<TToken, TTokenKind> r, int offset, out Piece piece) {
        var tokens = new List<TToken>();
        var children = new List<MatchResult<TToken, TTokenKind>>();
        var pos = offset;
        var count = 0;
        if (_grammar.HasRule(r.RuleName)) {
            while (count < r.Max) {
                var item = TryMatchRuleAt(r.RuleName, pos);
                if (item is null || item.Consumed == 0)
                    break;
                tokens.AddRange(item.Tokens);
                children.Add(item);
                pos += item.Consumed;
                count++;
            }
        }
        if (count < r.Min) {
            piece = default;
            return false;
        }
        piece = new Piece(tokens, children, []);
        return true;
    }

    private bool TryLeftAssoc(LeftAssoc<TToken, TTokenKind> la, int offset, out Piece piece) {
        var first = TryMatchRuleAt(la.OperandRule, offset);
        if (first is null || first.Consumed == 0) {
            piece = default;
            return false;
        }
        var tokens = new List<TToken>(first.Tokens);
        var children = new List<MatchResult<TToken, TTokenKind>> { first };
        var operators = new List<TToken>();
        var pos = offset + first.Consumed;
        while (true) {
            var op = _reader.Peek(pos);
            var opMatched = false;
            foreach (var kind in la.OperatorKinds) {
                if (EqualityComparer<TTokenKind>.Default.Equals(op.Kind, kind)) {
                    opMatched = true;
                    break;
                }
            }
            if (!opMatched) break;

            var next = TryMatchRuleAt(la.OperandRule, pos + 1);
            if (next is null || next.Consumed == 0) {
                piece = default;
                return false;
            }
            operators.Add(op);
            children.Add(next);
            tokens.Add(op);
            tokens.AddRange(next.Tokens);
            pos += 1 + next.Consumed;
        }
        piece = new Piece(tokens, children, operators);
        return true;
    }

    private bool TryBalanced(Balanced<TToken, TTokenKind> b, int offset, out List<TToken> consumed) {
        var first = _reader.Peek(offset);
        if (!EqualityComparer<TTokenKind>.Default.Equals(first.Kind, b.Open)) {
            Fail(b, offset, b.Open);
            consumed = [];
            return false;
        }
        var tokens = new List<TToken> { first };
        var pos = offset + 1;
        var depth = 1;
        while (true) {
            var t = _reader.Peek(pos);
            if (_reader.EndOfStream(t.Kind)) {
                Fail(b, pos, b.Close);
                consumed = [];
                return false;
            }
            tokens.Add(t);
            pos++;
            if (EqualityComparer<TTokenKind>.Default.Equals(t.Kind, b.Open)) {
                depth++;
            }
            else if (EqualityComparer<TTokenKind>.Default.Equals(t.Kind, b.Close)) {
                depth--;
                if (depth == 0) break;
            }
        }
        consumed = tokens;
        return true;
    }

    // ── the failure report (Match only) ──────────────────────────────────────────────

    /// <summary>Records a failing token element at <paramref name="offset"/> (no-op outside <see cref="Match"/>).</summary>
    private void Fail(IPatternElement<TToken, TTokenKind> element, int offset, TTokenKind? kind) {
        if (_tracker is { } tracker)
            tracker.Record(new Entry(_frame.Rule, _frame.Pattern, _frame.Index, element, offset, kind, _frame.Captures));
    }

    /// <summary>
    /// The committed failure of <paramref name="element"/>, which just failed after its row's Cut: its own failures,
    /// re-collected by matching it once more with a fresh accumulator (and, for an element that reaches other rules, a
    /// fresh memo, since a memo hit records nothing). A token element is just itself at <paramref name="pos"/>.
    /// </summary>
    private Commit CommitAt(Frame frame, IPatternElement<TToken, TTokenKind> element, int pos, List<TToken> matched) {
        var outer = _tracker;
        var outerMemo = _memo;
        var local = new FailureTracker();
        _tracker = local;
        _frame = frame;
        if (element is Ref<TToken, TTokenKind> or Repeat<TToken, TTokenKind> or LeftAssoc<TToken, TTokenKind>)
            _memo = new Dictionary<(string Rule, int Offset), MatchResult<TToken, TTokenKind>?>();
        try {
            _ = TryMatchElement(element, pos, frame.Captures, matched, out _);
        }
        finally {
            _tracker = outer;
            _memo = outerMemo;
        }
        return local.Far >= 0
            ? new Commit(local.Far, [.. local.Entries])
            : new Commit(pos, [new Entry(frame.Rule, frame.Pattern, frame.Index, element, pos, null, frame.Captures)]);
    }

    /// <summary>
    /// The report for <paramref name="entries"/> (all at the same offset): sorted by (declaration order, element
    /// index) and deduplicated; the first entry names the rule, row and element, and the first entry with a
    /// template (or the refusal) carries the message. With no entry the failure is at <paramref name="far"/>
    /// (the head when that is -1) and expects nothing.
    /// </summary>
    private MatchFailure<TToken, TTokenKind> Report(IReadOnlyList<Entry> entries, int far, bool committed) {
        var sorted = new List<Entry>(entries.Count);
        foreach (var entry in entries) {
            if (!sorted.Exists(e => ReferenceEquals(e.Pattern, entry.Pattern) && e.Index == entry.Index && ReferenceEquals(e.Element, entry.Element)))
                sorted.Add(entry);
        }
        sorted.Sort(static (a, b) => a.Pattern.Order != b.Pattern.Order ? a.Pattern.Order.CompareTo(b.Pattern.Order) : a.Index.CompareTo(b.Index));
        if (sorted.Count == 0) {
            var at = Math.Max(far, 0);
            return new MatchFailure<TToken, TTokenKind>(_reader.Peek(at), at, committed, refused: false, "", "", -1, [],
                template: null, new Dictionary<string, IReadOnlyList<TToken>>(StringComparer.Ordinal));
        }

        var first = sorted[0];
        var expected = new List<Expectation<TTokenKind>>(sorted.Count);
        Entry? messenger = null;
        foreach (var entry in sorted) {
            if (messenger is null && (entry.Element is Refuse<TToken, TTokenKind> || entry.Element is IExpectedElement { ExpectTemplate: not null }))
                messenger = entry;
            var label = LabelOf(entry.Element);
            if (label is not null || entry.Kind is not null)
                expected.Add(new Expectation<TTokenKind>(entry.Rule, entry.Pattern.Name, entry.Index, label, entry.Kind, IsContent(entry.Element)));
        }
        var refusal = messenger?.Element as Refuse<TToken, TTokenKind>;
        var template = refusal?.Message ?? (messenger?.Element as IExpectedElement)?.ExpectTemplate;
        var source = messenger ?? first;
        return new MatchFailure<TToken, TTokenKind>(
            _reader.Peek(source.Offset), source.Offset, committed, refusal is not null,
            first.Rule, first.Pattern.Name, first.Index, expected, template,
            source.Captures ?? new Dictionary<string, IReadOnlyList<TToken>>(StringComparer.Ordinal));
    }

    /// <summary>What a report calls an element: its Expect label, a Word's quoted text, a Run's name; else null (the host names it by kind).</summary>
    private static string? LabelOf(IPatternElement<TToken, TTokenKind> element) => element switch {
        IExpectedElement { Expect: { } label } => label,
        Word<TToken, TTokenKind> w => $"'{w.Text}'",
        Run<TToken, TTokenKind> r => r.Name,
        _ => null,
    };

    /// <summary>A content element (runtime text: a value, predicate, Run or Any), as opposed to fixed syntax.</summary>
    private static bool IsContent(IPatternElement<TToken, TTokenKind> element) =>
        element is Value<TToken, TTokenKind> or MatchPredicate<TToken, TTokenKind> or Run<TToken, TTokenKind> or Any<TToken, TTokenKind>;

    /// <summary>The row element being matched (set per element inside <see cref="Match"/>).</summary>
    private readonly record struct Frame(
        string Rule,
        Pattern<TToken, TTokenKind> Pattern,
        int Index,
        Dictionary<string, IReadOnlyList<TToken>> Captures);

    /// <summary>One failing element: where it is in the table, the offset it failed at, the kind it expected (if one).</summary>
    private readonly record struct Entry(
        string Rule,
        Pattern<TToken, TTokenKind> Pattern,
        int Index,
        IPatternElement<TToken, TTokenKind> Element,
        int Offset,
        TTokenKind? Kind,
        IReadOnlyDictionary<string, IReadOnlyList<TToken>>? Captures);

    /// <summary>The furthest offset any element failed at, and the elements that failed there.</summary>
    private sealed class FailureTracker {
        public int Far { get; private set; } = -1;

        public List<Entry> Entries { get; } = [];

        public void Reset() {
            Far = -1;
            Entries.Clear();
        }

        public void Record(Entry entry) {
            if (entry.Offset > Far) {
                Far = entry.Offset;
                Entries.Clear();
            }
            if (entry.Offset == Far)
                Entries.Add(entry);
        }
    }

    /// <summary>
    /// A committed failure: <see cref="Reach"/> is how far the row got (the failing offset, or for a refusal the
    /// position after its last token), so a sibling rescues it only by matching past it; the furthest commits union.
    /// </summary>
    private sealed class Commit(int reach, List<Entry> entries) {
        public int Reach { get; } = reach;

        public List<Entry> Entries { get; } = entries;

        public static void Add(ref Commit? existing, Commit next) {
            if (existing is null || next.Reach > existing.Reach) {
                existing = next;
                return;
            }
            if (next.Reach == existing.Reach)
                existing.Entries.AddRange(next.Entries);
        }
    }

    /// <summary>An unrescued commit leaving its rule: aborts the Match call (caught there only).</summary>
    private sealed class CommitAbort(MatchFailure<TToken, TTokenKind> failure) : Exception {
        public MatchFailure<TToken, TTokenKind> Failure { get; } = failure;
    }

    private readonly struct Piece(
        List<TToken> tokens,
        List<MatchResult<TToken, TTokenKind>> children,
        List<TToken> operators) {
        public List<TToken> Tokens { get; } = tokens;
        public List<MatchResult<TToken, TTokenKind>> Children { get; } = children;
        public List<TToken> Operators { get; } = operators;

        public static Piece Empty { get; } = new([], [], []);

        public static Piece TokensOnly(List<TToken> tokens) => new(tokens, [], []);

        public static Piece Child(MatchResult<TToken, TTokenKind> child) =>
            new([.. child.Tokens], [child], []);
    }
}