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
    /// </summary>
    public MatchResult<TToken, TTokenKind>? TryMatch(string ruleName) =>
        TryMatchRuleAt(ruleName, offset: 0);

    private void EnsureKnownRule(string ruleName) {
        if (!_grammar.HasRule(ruleName))
            throw new ArgumentException($"Unknown grammar rule '{ruleName}'", nameof(ruleName));
    }

    private MatchResult<TToken, TTokenKind>? TryMatchRuleAt(string ruleName, int offset) {
        EnsureKnownRule(ruleName);
        MatchResult<TToken, TTokenKind>? best = null;
        var bestPriority = int.MinValue;
        foreach (var pattern in _grammar.GetPatterns(ruleName)) {
            if (!TryMatchPattern(ruleName, pattern, offset, out var match))
                continue;
            if (best is null
                || match.Consumed > best.Consumed
                || (match.Consumed == best.Consumed && pattern.Priority > bestPriority)) {
                best = match;
                bestPriority = pattern.Priority;
            }
        }
        return best;
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
        out MatchResult<TToken, TTokenKind> match) {
        var tokens = new List<TToken>();
        var children = new List<MatchResult<TToken, TTokenKind>>();
        var operators = new List<TToken>();
        var captures = new Dictionary<string, IReadOnlyList<TToken>>(StringComparer.Ordinal);
        var pos = offset;
        foreach (var element in pattern.Elements) {
            if (!TryMatchElement(element, pos, captures, out var piece)) {
                match = null!;
                return false;
            }
            tokens.AddRange(piece.Tokens);
            children.AddRange(piece.Children);
            operators.AddRange(piece.Operators);
            pos += piece.Tokens.Count;
        }
        match = new MatchResult<TToken, TTokenKind>(
            pattern.Name, tokens, captures, ruleName, children, operators);
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

    private bool TryMatchElement(
        IPatternElement<TToken, TTokenKind> element,
        int offset,
        Dictionary<string, IReadOnlyList<TToken>> captures,
        out Piece piece) {
        switch (element) {
            case MatchKind<TToken, TTokenKind> k:
                if (!TryMatchKind(k.Kind, offset, out var kindTokens)) {
                    piece = default;
                    return false;
                }
                piece = Piece.TokensOnly(kindTokens);
                return true;

            case Value<TToken, TTokenKind> v:
                if (!TryMatchKind(v.Kind, offset, out var valueTokens)) {
                    piece = default;
                    return false;
                }
                RecordCapture(captures, v.Name, valueTokens);
                piece = Piece.TokensOnly(valueTokens);
                return true;

            case MatchPredicate<TToken, TTokenKind> p:
                var pt = _reader.Peek(offset);
                if (!p.Predicate(pt)) {
                    piece = default;
                    return false;
                }
                List<TToken> predTokens = [pt];
                RecordCapture(captures, p.Label, predTokens);
                piece = Piece.TokensOnly(predTokens);
                return true;

            case Optional<TToken, TTokenKind> o:
                if (TryMatchElement(o.Inner, offset, captures, out piece))
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
                    piece = default;
                    return false;
                }
                piece = Piece.TokensOnly([at]);
                return true;

            case NotFollowedBy<TToken, TTokenKind> n:
                var next = _reader.Peek(offset);
                if (EqualityComparer<TTokenKind>.Default.Equals(next.Kind, n.Kind)) {
                    piece = default;
                    return false;
                }
                piece = Piece.Empty;
                return true;

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
            consumed = [];
            return false;
        }
        var tokens = new List<TToken> { first };
        var pos = offset + 1;
        var depth = 1;
        while (true) {
            var t = _reader.Peek(pos);
            if (_reader.EndOfStream(t.Kind)) {
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