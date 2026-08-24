namespace Poly.Grammar;

/// <summary>
/// Longest-match pattern scanner over a token stream. Recognition only — matched
/// token sequences are returned; folding into IR is the handler's job.
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

    /// <summary>
    /// Attempts to match the named rule at the current position. The reader is only
    /// examined, never consumed — callers commit a successful match via
    /// <c>reader.Consume(match.Consumed)</c>.
    ///
    /// True longest-match: every pattern in the rule is tried and the one consuming
    /// the most tokens wins (not merely the first in sorted order). Sorting still
    /// orders patterns for <see cref="Repeat{TToken,TTokenKind}"/> (first-match in
    /// sorted order is intended there — longer element count first).
    /// </summary>
    public MatchResult<TToken, TTokenKind>? TryMatch(string ruleName) {
        EnsureKnownRule(ruleName);
        MatchResult<TToken, TTokenKind>? best = null;
        var bestPriority = int.MinValue;
        foreach (var pattern in _grammar.GetPatterns(ruleName)) {
            var captures = new Dictionary<string, IReadOnlyList<TToken>>(StringComparer.Ordinal);
            if (TryMatchPattern(pattern, 0, out var tokens, captures)) {
                if (best is null
                    || tokens.Count > best.Consumed
                    || (tokens.Count == best.Consumed && pattern.Priority > bestPriority)) {
                    best = new MatchResult<TToken, TTokenKind>(pattern.Name, tokens, captures);
                    bestPriority = pattern.Priority;
                }
            }
        }
        return best;
    }

    private void EnsureKnownRule(string ruleName) {
        if (!_grammar.HasRule(ruleName))
            throw new ArgumentException($"Unknown grammar rule '{ruleName}'", nameof(ruleName));
    }

    private bool TryMatchRule(string ruleName, int offset, out List<TToken> consumed) {
        EnsureKnownRule(ruleName);
        List<TToken>? best = null;
        foreach (var pattern in _grammar.GetPatterns(ruleName)) {
            if (TryMatchPattern(pattern, offset, out var tokens, captures: null)) {
                if (best is null || tokens.Count > best.Count)
                    best = tokens;
            }
        }
        if (best is null) {
            consumed = [];
            return false;
        }
        consumed = best;
        return true;
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
        Pattern<TToken, TTokenKind> pattern,
        int offset,
        out List<TToken> consumed,
        Dictionary<string, IReadOnlyList<TToken>>? captures) {
        var tokens = new List<TToken>();
        var pos = offset;
        foreach (var element in pattern.Elements) {
            if (!TryMatchElement(element, pos, out var elementTokens, captures)) {
                consumed = [];
                return false;
            }
            tokens.AddRange(elementTokens);
            pos += elementTokens.Count;
        }
        consumed = tokens;
        return true;
    }

    private void RecordCapture(Dictionary<string, IReadOnlyList<TToken>>? captures, string? name, IReadOnlyList<TToken> tokens) {
        if (captures is null || string.IsNullOrEmpty(name) || tokens.Count == 0)
            return;
        captures[name] = tokens;
    }

    private bool TryMatchElement(
        IPatternElement<TToken, TTokenKind> element,
        int offset,
        out List<TToken> consumed,
        Dictionary<string, IReadOnlyList<TToken>>? captures) {
        switch (element) {
            case MatchKind<TToken, TTokenKind> k:
                return TryMatchKind(k.Kind, offset, consumed: out consumed);

            case Value<TToken, TTokenKind> v:
                if (!TryMatchKind(v.Kind, offset, consumed: out consumed))
                    return false;
                RecordCapture(captures, v.Name, consumed);
                return true;

            case MatchPredicate<TToken, TTokenKind> p:
                var pt = _reader.Peek(offset);
                if (!p.Predicate(pt)) {
                    consumed = [];
                    return false;
                }
                consumed = [pt];
                RecordCapture(captures, p.Label, consumed);
                return true;

            case Optional<TToken, TTokenKind> o:
                if (TryMatchElement(o.Inner, offset, out var optTokens, captures)) {
                    consumed = optTokens;
                    return true;
                }
                consumed = [];
                return true;

            case Repeat<TToken, TTokenKind> r:
                return TryRepeat(r, offset, out consumed);

            case Ref<TToken, TTokenKind> rr:
                if (!TryMatchRule(rr.RuleName, offset, out consumed))
                    return false;
                if (consumed.Count == 0)
                    return false; // zero-width sub-match: failure (infinite-recursion guard)
                return true;

            case LeftAssoc<TToken, TTokenKind> la:
                return TryLeftAssoc(la, offset, out consumed);

            case Balanced<TToken, TTokenKind> b:
                return TryBalanced(b, offset, out consumed);

            case Any<TToken, TTokenKind>:
                var at = _reader.Peek(offset);
                // Any must not match end-of-stream — guards scan loops against
                // consuming EOF forever (mirrors v1 AnyToken semantics).
                if (_reader.EndOfStream(at.Kind)) {
                    consumed = [];
                    return false;
                }
                consumed = [at];
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

    private bool TryRepeat(Repeat<TToken, TTokenKind> r, int offset, out List<TToken> consumed) {
        var tokens = new List<TToken>();
        var pos = offset;
        var count = 0;
        var subPatterns = _grammar.GetPatterns(r.RuleName);
        while (count < r.Max) {
            var matched = false;
            foreach (var sub in subPatterns) {
                if (TryMatchPattern(sub, pos, out var subTokens, captures: null)) {
                    if (subTokens.Count == 0)
                        break; // zero-width sub-match: stop, avoid infinite loop
                    tokens.AddRange(subTokens);
                    pos += subTokens.Count;
                    count++;
                    matched = true;
                    break;
                }
            }
            if (!matched) break;
        }
        if (count < r.Min) {
            consumed = [];
            return false;
        }
        consumed = tokens;
        return true;
    }

    private bool TryLeftAssoc(LeftAssoc<TToken, TTokenKind> la, int offset, out List<TToken> consumed) {
        // First operand uses Ref semantics (zero-width fails — recursion guard).
        if (!TryMatchRule(la.OperandRule, offset, out var first) || first.Count == 0) {
            consumed = [];
            return false;
        }
        var tokens = new List<TToken>(first);
        var pos = offset + first.Count;
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

            // Trailing operator without a (non-empty) operand fails the whole chain.
            if (!TryMatchRule(la.OperandRule, pos + 1, out var next) || next.Count == 0) {
                consumed = [];
                return false;
            }
            tokens.Add(op);
            tokens.AddRange(next);
            pos += 1 + next.Count;
        }
        consumed = tokens;
        return true;
    }

    private bool TryBalanced(Balanced<TToken, TTokenKind> b, int offset, out List<TToken> consumed) {
        // The span must START with the opening delimiter — Balanced does not scan
        // forward past leading content (that is the enclosing pattern's job).
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
                return false; // input ended without the closing delimiter
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
}