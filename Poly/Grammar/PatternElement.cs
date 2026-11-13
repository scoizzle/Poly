namespace Poly.Grammar;

/// <summary>
/// A single element in a <see cref="Pattern{TToken,TTokenKind}"/>: describes what
/// token sequence it expects at the current scan position. Recognition only —
/// folding matches into IR is the handler's job.
/// </summary>
public interface IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// A token element a failure report can name (<see cref="MatchKind{TToken,TTokenKind}"/>,
/// <see cref="Value{TToken,TTokenKind}"/>, <see cref="MatchPredicate{TToken,TTokenKind}"/>,
/// <see cref="Word{TToken,TTokenKind}"/>, <see cref="Run{TToken,TTokenKind}"/>). <see cref="Expect"/> is what the
/// report calls the element when it is expected and does not match (a predicate's <c>Label</c> is a capture
/// name, so the failure label is a field of its own); <see cref="ExpectTemplate"/> is the row's own message for
/// that failure, with the placeholders <c>{expected}</c>, <c>{got}</c> and a capture name of a required element
/// before it. Set both with <see cref="PatternBuilder{TToken,TTokenKind}.Expect"/>.
/// </summary>
public interface IExpectedElement {
    /// <summary>The label a failure report shows for this element; null for the element's default.</summary>
    string? Expect { get; }

    /// <summary>The row's message when this element is the first expected element of a failure; null for the host's default.</summary>
    string? ExpectTemplate { get; }
}

/// <summary>Matches a single token of a specific kind (fixed syntax — auto-emitted by printer).</summary>
public sealed record MatchKind<TToken, TTokenKind>(TTokenKind Kind) : IPatternElement<TToken, TTokenKind>, IExpectedElement
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    public string? Expect { get; init; }
    public string? ExpectTemplate { get; init; }
}

/// <summary>
/// Matches a single value-bearing token of a specific kind (runtime content).
/// <paramref name="Name"/> is the capture / print-fill key; null is unnamed (legacy callback).
/// </summary>
public sealed record Value<TToken, TTokenKind>(TTokenKind Kind, string? Name = null)
    : IPatternElement<TToken, TTokenKind>, IExpectedElement
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    public string? Expect { get; init; }
    public string? ExpectTemplate { get; init; }
}

/// <summary>Matches a single token whose full content satisfies a predicate (semantic predicates live here).</summary>
public sealed record MatchPredicate<TToken, TTokenKind>(
    Func<TToken, bool> Predicate,
    string Label
) : IPatternElement<TToken, TTokenKind>, IExpectedElement
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    public string? Expect { get; init; }
    public string? ExpectTemplate { get; init; }
}

/// <summary>Matches zero or one occurrence of an inner element.</summary>
public sealed record Optional<TToken, TTokenKind>(IPatternElement<TToken, TTokenKind> Inner)
    : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Matches between <paramref name="Min"/> and <paramref name="Max"/> occurrences of
/// patterns from the named rule. Bounded repetition by construction — no hard
/// iteration cap. A zero-width sub-match stops the loop (infinite-recursion guard);
/// fewer than <paramref name="Min"/> matches fails the element.
/// </summary>
public sealed record Repeat<TToken, TTokenKind>(
    string RuleName,
    int Min = 0,
    int Max = int.MaxValue
) : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Matches exactly one occurrence of the named rule (recursive / nested languages).
/// A sub-match consuming zero tokens is treated as failure (infinite-recursion guard).
/// </summary>
public sealed record Ref<TToken, TTokenKind>(string RuleName) : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Matches a left-associative chain: one <paramref name="OperandRule"/> match, then
/// while an operator kind from <paramref name="OperatorKinds"/> matches, another
/// operand. The full span (operands + operators) is accumulated flat; folding into
/// IR is the handler's job (operator identity recoverable from kinds). A trailing
/// operator with no following operand fails the whole element.
/// </summary>
public sealed record LeftAssoc<TToken, TTokenKind>(
    string OperandRule,
    IReadOnlyList<TTokenKind> OperatorKinds
) : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Matches brace-balanced content. The token at the current position MUST be
/// <paramref name="Open"/> — Balanced does not scan forward past leading content
/// (that is the enclosing pattern's job). Nesting depth is tracked so the first
/// matching <paramref name="Close"/> at depth 0 terminates the span. End-of-stream
/// before the close fails the element.
/// </summary>
public sealed record Balanced<TToken, TTokenKind>(TTokenKind Open, TTokenKind Close)
    : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>Matches any single token (wildcard).</summary>
public sealed record Any<TToken, TTokenKind> : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Zero-width negative lookahead: succeeds when the token at this position is
/// not <paramref name="Kind"/> (including end-of-stream). Consumes nothing.
/// </summary>
public sealed record NotFollowedBy<TToken, TTokenKind>(TTokenKind Kind)
    : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Matches one token that <paramref name="Matches"/> accepts: a contextual word, such as an identifier
/// spelled <c>via</c>. The host supplies the test (the engine knows only token kinds); it captures nothing,
/// and the printer writes <paramref name="Text"/> as a token of <paramref name="Kind"/>, so the row owns
/// the spelling. A failure report calls it by its quoted text (<c>'via'</c>) unless <see cref="Expect"/> names it.
/// </summary>
public sealed record Word<TToken, TTokenKind>(TTokenKind Kind, string Text, Func<TToken, bool> Matches)
    : IPatternElement<TToken, TTokenKind>, IExpectedElement
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    public string? Expect { get; init; }
    public string? ExpectTemplate { get; init; }
}

/// <summary>
/// A zero-width print hole at the end of a row: the reader stops here (the host reads what follows, such
/// as an expression, with its own parser) and the printer writes the fill <paramref name="Name"/>, so the
/// writer spaces it like any token. A Slot is the row's last element and is never inside an
/// <see cref="Optional{TToken,TTokenKind}"/> (the builder rejects both).
/// </summary>
public sealed record Slot<TToken, TTokenKind>(string Name) : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Matches one or more tokens that continue the token before them, captured as <paramref name="Name"/>
/// (and printed from that fill). <paramref name="Continues"/> is <c>(previous, candidate, index)</c>: the
/// host decides what continues (the DSL: the same line, and adjacent from index 1), so the engine stays
/// position-free. The first candidate's previous token is the last token this row has matched, so a Run
/// is never the first element, never preceded only by optional elements, and never optional (the builder
/// rejects each); a Run that matches no token fails. A failure report calls it by its name unless
/// <see cref="Expect"/> names it.
/// </summary>
public sealed record Run<TToken, TTokenKind>(Func<TToken, TToken, int, bool> Continues, string Name)
    : IPatternElement<TToken, TTokenKind>, IExpectedElement
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    public string? Expect { get; init; }
    public string? ExpectTemplate { get; init; }
}

/// <summary>
/// Zero-width commit: once the elements before it have matched, a failure of any element after it is a
/// committed failure. A sibling row of the same rule rescues it only by matching more tokens than the
/// committed row had matched when it failed; otherwise it escapes the rule and aborts the whole
/// <see cref="Matcher{TToken,TTokenKind}.Match"/> call (outer siblings are not tried, and it is never
/// memoised). A row holds at most one Cut, never first and never last, never inside an
/// <see cref="Optional{TToken,TTokenKind}"/>; it prints nothing. A rule that holds one, directly or through a
/// rule it references, is matched with <c>Match</c>: <c>TryMatch</c> throws for it.
/// </summary>
public sealed record Cut<TToken, TTokenKind> : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>
/// Zero-width, terminal refusal: a row that reaches it matches its tokens and yields a committed failure with
/// <paramref name="Message"/> (placeholders <c>{got}</c> and a capture name of a required element before it),
/// positioned at the last token the row matched. It counts as matching every token up to there, so a
/// shorter sibling does not rescue it. It is the row's last element, after at least one required element or
/// alone (a catch-all that matches no token: it stands on the head token, and any sibling that matches a token
/// rescues it), never inside an <see cref="Optional{TToken,TTokenKind}"/>; a refusal row is never printed (the printer throws).
/// </summary>
public sealed record Refuse<TToken, TTokenKind>(string Message) : IPatternElement<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;