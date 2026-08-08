namespace Poly.Grammar;

/// <summary>
/// The core scan engine. Given a <see cref="Grammar{TKind}"/> and a
/// <see cref="TokenReader{TKind}"/>, performs a longest-match linear scan:
/// at each position, tries all patterns in the active rule, picks the one that
/// consumes the most tokens, advances the reader, and returns the result.
///
/// This single loop replaces recursive-descent parsers. Grammar rules describe
/// what patterns are valid at each position; the matcher discovers which one
/// matches.
/// </summary>
public sealed class Matcher<TKind> where TKind : struct {
    private readonly Grammar<TKind> _grammar;
    private readonly TokenReader<TKind> _reader;

    /// <param name="grammar">The grammar describing valid patterns.</param>
    /// <param name="reader">The token source to scan.</param>
    public Matcher(Grammar<TKind> grammar, TokenReader<TKind> reader) {
        _grammar = grammar ?? throw new ArgumentNullException(nameof(grammar));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    // ═══════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Attempts to match a pattern from the named rule at the current reader
    /// position. Returns the longest match, or <c>null</c> if no pattern matches.
    /// Throws <see cref="ArgumentException"/> when <paramref name="ruleName"/>
    /// is not defined (N3): a typo'd rule name must fail at the source, not
    /// silently never-match.
    ///
    /// On success the reader is <b>not</b> advanced — call <see cref="Consume"/>
    /// to advance past the matched tokens.
    /// </summary>
    public MatchResult<TKind>? TryMatch(string ruleName) {
        EnsureKnownRule(ruleName);
        var patterns = _grammar.GetPatterns(ruleName);
        MatchResult<TKind>? best = null;

        foreach (var pattern in patterns) {
            if (TryMatchPattern(pattern, 0, out var tokens)) {
                if (best == null || tokens.Count > best.Consumed)
                    best = new MatchResult<TKind>(pattern.Name, tokens);
            }
        }

        return best;
    }

    /// <summary>N3: fail loud on typo'd rule names (RuleRef / LeftAssoc / TryMatch).</summary>
    private void EnsureKnownRule(string ruleName) {
        if (!_grammar.HasRule(ruleName))
            throw new ArgumentException($"Unknown grammar rule '{ruleName}'", nameof(ruleName));
    }

    /// <summary>Advances the reader past the tokens consumed by <paramref name="result"/>.</summary>
    public void Consume(MatchResult<TKind> result) {
        for (int i = 0; i < result.Consumed; i++)
            _reader.Read();
    }

    /// <summary>
    /// Returns the distinct first-token kinds that can start a pattern in the
    /// named rule. Used for error messages and introspection.
    ///
    /// Only patterns whose first element is a concrete <see cref="MatchToken{TKind}"/>
    /// or <see cref="MatchValue{TKind}"/> are included; predicate-led patterns
    /// contribute to "expected" through runtime evaluation.
    /// </summary>
    public IEnumerable<TKind> ExpectedTokens(string ruleName) {
        var seen = new HashSet<TKind>();
        foreach (var pattern in _grammar.GetPatterns(ruleName)) {
            if (pattern.Elements.Count > 0) {
                if (pattern.Elements[0] is MatchToken<TKind> mt)
                    seen.Add(mt.Kind);
                else if (pattern.Elements[0] is MatchValue<TKind> mv)
                    seen.Add(mv.Kind);
            }
        }
        return seen;
    }

    /// <summary>
    /// Reads the next token unconditionally (advancing the reader) and returns it.
    /// Useful when the caller knows what token kind to expect and the grammar
    /// pattern can't express the full shape.
    /// </summary>
    public Token<TKind> Read() => _reader.Read();

    /// <summary>
    /// Peeks at the nth future token without consuming. Pass-through to the
    /// underlying reader for use in handlers that need extra lookahead.
    /// </summary>
    public Token<TKind> Peek(int n = 1) => _reader.Peek(n);

    // ═══════════════════════════════════════════════════════════
    //  Internal matching
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Tries to match a pattern starting at <paramref name="offset"/> tokens
    /// ahead of the current reader position. Returns <c>true</c> with the
    /// consumed tokens, or <c>false</c> if any element fails.
    /// </summary>
    private bool TryMatchPattern(Pattern<TKind> pattern, int offset, out IReadOnlyList<Token<TKind>> consumed) {
        var tokens = new List<Token<TKind>>();
        var pos = offset;

        foreach (var element in pattern.Elements) {
            if (!TryMatchElement(element, pos, out var elementTokens)) {
                consumed = [];
                return false;
            }
            tokens.AddRange(elementTokens);
            pos += elementTokens.Count;
        }

        consumed = tokens;
        return true;
    }

    /// <summary>
    /// Matches the named rule relative to <paramref name="offset"/> using the
    /// same longest-match selection as <see cref="TryMatch"/>. Zero-width
    /// sub-matches fail (infinite recursion guard). Shared by
    /// <see cref="RuleRef{TKind}"/> and <see cref="LeftAssoc{TKind}"/> operands.
    /// Throws for unknown rule names (N3) — a RuleRef/LeftAssoc typo must fail
    /// at the source, never silently never-match.
    /// </summary>
    private bool TryMatchRule(string ruleName, int offset, out IReadOnlyList<Token<TKind>> consumed) {
        EnsureKnownRule(ruleName);
        MatchResult<TKind>? best = null;
        foreach (var sub in _grammar.GetPatterns(ruleName)) {
            if (TryMatchPattern(sub, offset, out var subTokens)) {
                if (best == null || subTokens.Count > best.Consumed)
                    best = new MatchResult<TKind>(sub.Name, subTokens);
            }
        }
        if (best is null || best.Consumed == 0) {
            consumed = [];
            return false;
        }
        consumed = best.Tokens;
        return true;
    }

    /// <summary>
    /// Dispatches a single <see cref="IPatternElement{TKind}"/> at the given offset.
    /// </summary>
    private bool TryMatchElement(IPatternElement<TKind> element, int offset, out IReadOnlyList<Token<TKind>> consumed) {
        switch (element) {
            case MatchToken<TKind> mt:
            case MatchValue<TKind> mv: {
                    var kind = element is MatchToken<TKind> t ? t.Kind : ((MatchValue<TKind>)element).Kind;
                    var tk = _reader.Peek(offset + 1);
                    if (EqualityComparer<TKind>.Default.Equals(tk.Kind, kind)) {
                        consumed = [tk];
                        return true;
                    }
                    consumed = [];
                    return false;
                }

            case MatchPredicate<TKind> mp: {
                    var tp = _reader.Peek(offset + 1);
                    if (mp.Predicate(tp.Kind)) {
                        consumed = [tp];
                        return true;
                    }
                    consumed = [];
                    return false;
                }

            case Optional<TKind> opt:
                if (TryMatchElement(opt.Inner, offset, out var optTokens)) {
                    consumed = optTokens;
                }
                else {
                    consumed = [];
                }
                return true;

            case AnyToken<TKind>: {
                    var wild = _reader.Peek(offset + 1);
                    // Don't match virtual end-of-file tokens — prevents infinite
                    // scan loops that would keep consuming cached EOFs.
                    if (_reader.IsEndOfFile(wild.Kind)) {
                        consumed = [];
                        return false;
                    }
                    consumed = [wild];
                    return true;
                }

            case ManyOf<TKind> many: {
                    var manyTokens = new List<Token<TKind>>();
                    var manyPos = offset;
                    var maxIter = 10_000;
                    for (var iter = 0; iter < maxIter; iter++) {
                        var subPatterns = _grammar.GetPatterns(many.RuleName);
                        var matched = false;
                        foreach (var sub in subPatterns) {
                            if (TryMatchPattern(sub, manyPos, out var subTokens) && subTokens.Count > 0) {
                                manyTokens.AddRange(subTokens);
                                manyPos += subTokens.Count;
                                matched = true;
                                break;
                            }
                        }
                        if (!matched) break;
                    }
                    consumed = manyTokens;
                    return true;
                }

            case RuleRef<TKind> rr:
                return TryMatchRule(rr.RuleName, offset, out consumed);

            case LeftAssoc<TKind> la: {
                    // First operand uses Rule(operandRule) semantics (zero-width fails).
                    if (!TryMatchRule(la.OperandRule, offset, out var first)) {
                        consumed = [];
                        return false;
                    }
                    var tokens = new List<Token<TKind>>(first);
                    var pos = offset + first.Count;
                    for (var iter = 0; iter < 10_000; iter++) {
                        var op = _reader.Peek(pos + 1);
                        var opMatched = false;
                        foreach (var kind in la.OperatorKinds) {
                            if (EqualityComparer<TKind>.Default.Equals(op.Kind, kind)) {
                                opMatched = true;
                                break;
                            }
                        }
                        if (!opMatched) break;

                        // Trailing operator without an operand fails the whole chain.
                        if (!TryMatchRule(la.OperandRule, pos + 1, out var next)) {
                            consumed = [];
                            return false;
                        }
                        tokens.Add(op);
                        tokens.AddRange(next);
                        pos += 1 + next.Count;
                    }
                    // N1: fail closed at the iteration cap — a chain still
                    // operator-led after 10_000 pairs is a truncated match or a
                    // trailing operator, both malformed. (ManyOf keeps a
                    // fail-open cap; LeftAssoc's trailing-op rule makes failure
                    // the only consistent choice.)
                    var capOp = _reader.Peek(pos + 1);
                    foreach (var kind in la.OperatorKinds) {
                        if (EqualityComparer<TKind>.Default.Equals(capOp.Kind, kind)) {
                            consumed = [];
                            return false;
                        }
                    }
                    consumed = tokens;
                    return true;
                }

            case Balanced<TKind> bal: {
                    var open = _reader.Peek(offset + 1);
                    if (!EqualityComparer<TKind>.Default.Equals(open.Kind, bal.Open)) {
                        consumed = [];
                        return false;
                    }
                    var balTokens = new List<Token<TKind>> { open };
                    var depth = 1;
                    var balPos = offset + 1;
                    while (depth > 0) {
                        balPos++;
                        var next = _reader.Peek(balPos);
                        // Guard: if we've hit end-of-file without a matching close,
                        // bail out — don't infinite-loop on cached EOF tokens.
                        if (_reader.IsEndOfFile(next.Kind)) {
                            consumed = [];
                            return false;
                        }
                        balTokens.Add(next);
                        if (EqualityComparer<TKind>.Default.Equals(next.Kind, bal.Open))
                            depth++;
                        else if (EqualityComparer<TKind>.Default.Equals(next.Kind, bal.Close))
                            depth--;
                    }
                    consumed = balTokens;
                    return true;
                }

            default:
                consumed = [];
                return false;
        }
    }
}