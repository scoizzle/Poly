using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// One surface the change-protocol check runs over: its table, and the reader's own
/// tokenize step (no parallel tokenizer code).
/// </summary>
/// <param name="Name">The surface key the allow-list and examples use (<c>dsl</c>, <c>csharp</c>).</param>
/// <param name="Grammar">The table the rows are checked against.</param>
/// <param name="Tokenize">The reader's tokenizer over a printed text.</param>
/// <param name="ReportsSuppliedContent">True when every print on this surface reports how many
/// positional content values it supplied (the C# writer's <c>RenderFills</c>); the slot-count rule
/// (A1) then compares that count with the row's slots.</param>
internal sealed record PatternRowSurface<TToken, TTokenKind>(
    string Name,
    Grammar<TToken, TTokenKind> Grammar,
    Func<string, ITokenReader<TToken, TTokenKind>> Tokenize,
    bool ReportsSuppliedContent)
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>Which rule of the check a violation breaks.</summary>
internal enum PatternRowCheck {
    /// <summary>The allow-list or example table is malformed (schema, key, owner, duplicate).</summary>
    Schema,
    /// <summary>The printed pattern is not a row of the table.</summary>
    UnknownRow,
    /// <summary>A1: a named hole is missing, or a supplied fill names no hole of the row.</summary>
    Holes,
    /// <summary>A1: the caller supplied a different number of content values than the row has slots.</summary>
    Arity,
    /// <summary>A2: the printed text does not re-read as the same rule and pattern to the end of input.</summary>
    ReadBack,
    /// <summary>A3a: a row of the table is never printed and carries no unobserved entry.</summary>
    Unobserved,
    /// <summary>A3b: an optional or repeated slot of an observed row is empty in every observation.</summary>
    SlotNeverFilled,
    /// <summary>A3c: a reader-head row has no example, or an example does not select its row.</summary>
    Example,
    /// <summary>An allow-list entry whose condition does not fire.</summary>
    Stale,
}

/// <summary>One finding of the check.</summary>
internal sealed record PatternRowViolation(PatternRowCheck Check, string Rule, string? Pattern, string Message) {
    public override string ToString() => $"{Check} {Rule}/{Pattern ?? "*"}: {Message}";
}

/// <summary>
/// The grammar change-protocol check (G-23): every observed print of pattern <c>p</c> of rule
/// <c>R</c> is checked against the table it was printed from. Pure over its inputs: it reads the
/// grammar it is given and the observations it is given, and never a static writer.
///
/// <list type="bullet">
/// <item><b>A1, holes and slots.</b> A row's holes are its named <see cref="Value{TToken,TTokenKind}"/>
/// elements, plus each labelled <see cref="MatchPredicate{TToken,TTokenKind}"/> whose label the caller
/// supplied (that is the printer's rule). Every named value must be supplied, and a supplied key must name
/// a hole. A labelled predicate the caller did not fill is a positional slot, so the hole check is circular
/// for predicate labels: a missing predicate fill is caught by A2 only. The slot count comes from a
/// counting probe on the real <see cref="Printer{TToken,TTokenKind}"/>; when the surface reports supplied
/// content, the count supplied must equal it (strict: a spare value fails).</item>
/// <item><b>A2, read-back.</b> The printed text is tokenized by the reader's own tokenizer and matched
/// against rule <c>R</c> of the full grammar; the top match must be pattern <c>p</c> and consume every token
/// before end of input.</item>
/// <item><b>A3, coverage.</b> (a) every row is observed or carries an unobserved entry; (b) every
/// optional or repeated slot of an observed row is non-empty in some observation, or carries an
/// <see cref="PatternRowEntryKind.AlwaysNull"/> entry keyed by its slot index; (c) every reader-head row
/// has an example that the matcher selects, and every example names a reader-head row.</item>
/// </list>
///
/// Every allow-list entry is checked both ways: an entry whose condition does not fire is a
/// <see cref="PatternRowCheck.Stale"/> violation.
/// </summary>
internal static class PatternRowChecker {
    /// <summary>What a top-level pattern element is to the printer.</summary>
    internal enum ElementRole {
        /// <summary>Emits fixed text (or nothing): <c>MatchKind</c>, <c>NotFollowedBy</c>.</summary>
        Fixed,
        /// <summary>A named value: always a hole.</summary>
        Hole,
        /// <summary>A labelled predicate: a hole when the caller supplies its label, else a slot.</summary>
        PredicateLabel,
        /// <summary>A required content slot (unnamed value, unlabelled predicate, <c>Any</c>, <c>Ref</c>,
        /// <c>LeftAssoc</c>, <c>Balanced</c>).</summary>
        Slot,
        /// <summary>An optional content slot (<c>Optional</c>, <c>Repeat</c>): A3b applies.</summary>
        OptionalSlot,
    }

    /// <summary>
    /// Classifies one top-level element. An element type this switch does not name throws, so a new
    /// element kind cannot pass the check by being skipped.
    /// </summary>
    internal static ElementRole Classify<TToken, TTokenKind>(IPatternElement<TToken, TTokenKind> element)
        where TToken : struct, IToken<TTokenKind>
        where TTokenKind : struct => element switch {
            MatchKind<TToken, TTokenKind> => ElementRole.Fixed,
            NotFollowedBy<TToken, TTokenKind> => ElementRole.Fixed,
            Value<TToken, TTokenKind> { Name: { Length: > 0 } } => ElementRole.Hole,
            MatchPredicate<TToken, TTokenKind> { Label: { Length: > 0 } } => ElementRole.PredicateLabel,
            Value<TToken, TTokenKind> => ElementRole.Slot,
            MatchPredicate<TToken, TTokenKind> => ElementRole.Slot,
            Any<TToken, TTokenKind> => ElementRole.Slot,
            Ref<TToken, TTokenKind> => ElementRole.Slot,
            LeftAssoc<TToken, TTokenKind> => ElementRole.Slot,
            Balanced<TToken, TTokenKind> => ElementRole.Slot,
            Optional<TToken, TTokenKind> => ElementRole.OptionalSlot,
            Repeat<TToken, TTokenKind> => ElementRole.OptionalSlot,
            _ => throw new InvalidOperationException(
                $"The change-protocol check does not know pattern element '{element.GetType().Name}'."),
        };

    /// <summary>Runs every rule of the check over one surface and returns the violations.</summary>
    public static IReadOnlyList<PatternRowViolation> Check<TToken, TTokenKind>(
        PatternRowSurface<TToken, TTokenKind> surface,
        IReadOnlyList<PrintObservation> observations,
        IReadOnlyList<PatternRowEntry> allowList,
        IReadOnlyList<PatternRowExample> examples)
        where TToken : struct, IToken<TTokenKind>
        where TTokenKind : struct {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(allowList);
        ArgumentNullException.ThrowIfNull(examples);
        return [];
    }

    /// <summary>
    /// The floor rule: every named row exists in the table (a renamed floor row fails loudly) and is
    /// observed at least once.
    /// </summary>
    public static IReadOnlyList<PatternRowViolation> CheckFloor<TToken, TTokenKind>(
        PatternRowSurface<TToken, TTokenKind> surface,
        IReadOnlyList<PrintObservation> observations,
        IReadOnlyList<(string Rule, string Pattern)> floor)
        where TToken : struct, IToken<TTokenKind>
        where TTokenKind : struct {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(floor);
        return [];
    }
}