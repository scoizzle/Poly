using Poly.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// V-03 (Q4): the unverified-write channel's shared vocabulary and its one range decision
/// procedure. <see cref="DecideRange"/> is consumed by <see cref="WriteRecorder"/>'s callers —
/// <c>EffectAnalyzer.ValidateAssignedValue</c> (every assign, through the postcondition or an
/// in-place inference) and <c>EffectAnalyzer.ValidateInitializerValue</c> (create/create-in
/// initializers) — and it reads absent endpoints explicitly (a null side on the target is
/// satisfied, a null side on the value against a closed target is Undecided, never Violated).
/// </summary>
internal static class WriteVerification {
    /// <summary>
    /// Decides whether a write of <paramref name="value"/> can satisfy <paramref name="target"/>.
    /// <list type="bullet">
    /// <item><see cref="ConstraintVerdict.Satisfied"/>: every bounded side of the value is inside
    /// the corresponding target bound.</item>
    /// <item><see cref="ConstraintVerdict.Violated"/>: the value's whole range is on one side of
    /// the target (or a target bound cannot be ordered, #367 fail-closed).</item>
    /// <item><see cref="ConstraintVerdict.Undecided"/>: the value carries no range, or a side is
    /// open against a closed target, or the ranges only partly overlap.</item>
    /// </list>
    /// </summary>
    internal static ConstraintVerdict DecideRange(RangeConstraint target, AbstractValue value) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);

        // #367: a target bound that cannot be ordered against a number is not skipped; nothing
        // numeric is in it, so the write is a definite violation (the caller keeps the error text).
        if (HasUnorderableBound(target)) return ConstraintVerdict.Violated;

        var range = value.Range;
        if (range is null) return ConstraintVerdict.Undecided; // no range: not verified, not violated

        // Hull endpoints (sound widening for a union's exact intervals).
        var vmin = range.Minimum;
        var vmax = range.Maximum;
        var tmin = target.Minimum;
        var tmax = target.Maximum;

        // Entirely outside: the value's whole range is on one side of the target.
        if (tmin is not null && vmax is not null && ConstraintMerge.TryCompare(vmax, tmin) is < 0)
            return ConstraintVerdict.Violated;
        if (tmax is not null && vmin is not null && ConstraintMerge.TryCompare(vmin, tmax) is > 0)
            return ConstraintVerdict.Violated;

        // Contained: every bounded side of the value is inside the target's bound. A null target
        // side is satisfied (no bound to break); a null value side against a closed target is not
        // contained, so the write is Undecided — never Violated (V-03 fork on #650/#507).
        var lowerOk = tmin is null || (vmin is not null && ConstraintMerge.TryCompare(vmin, tmin) is >= 0);
        var upperOk = tmax is null || (vmax is not null && ConstraintMerge.TryCompare(vmax, tmax) is <= 0);
        return lowerOk && upperOk ? ConstraintVerdict.Satisfied : ConstraintVerdict.Undecided;
    }

    /// <summary>
    /// Decides whether a write of <paramref name="value"/> can satisfy a Text <paramref name="target"/>
    /// length. <see cref="ConstraintVerdict.Satisfied"/> only when the value's own length range is
    /// inside the target's; otherwise <see cref="ConstraintVerdict.Undecided"/>, including a value
    /// that carries no length. Never <see cref="ConstraintVerdict.Violated"/> in this slice: a declared
    /// length is enforced at run time, so a disjoint pair (source length(10, 20) into length(1, 5)) is
    /// provable, but reporting it as an error is row V-08's remaining half. Until then it stays Undecided
    /// (a warning). A literal assigned directly is judged elsewhere, by <c>Evaluate</c>.
    /// </summary>
    internal static ConstraintVerdict DecideLength(LengthConstraint target, AbstractValue value) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);

        var length = value.Constraints.OfType<LengthConstraint>().FirstOrDefault();
        return length is not null && length.MinLength >= target.MinLength && length.MaxLength <= target.MaxLength
            ? ConstraintVerdict.Satisfied
            : ConstraintVerdict.Undecided;
    }

    /// <summary>
    /// Decides a write against a Text <paramref name="target"/> pattern: <see cref="ConstraintVerdict.Satisfied"/>
    /// only when the value carries the IDENTICAL declared pattern (it was already enforced when the value was
    /// written), otherwise <see cref="ConstraintVerdict.Undecided"/>; never Violated.
    /// </summary>
    internal static ConstraintVerdict DecidePattern(PatternConstraint target, AbstractValue value) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);

        return value.Constraints.OfType<PatternConstraint>()
            .Any(p => string.Equals(p.Pattern, target.Pattern, StringComparison.Ordinal))
            ? ConstraintVerdict.Satisfied
            : ConstraintVerdict.Undecided;
    }

    /// <summary>
    /// Decides a write against an <c>equals</c> target: <see cref="ConstraintVerdict.Satisfied"/> only when the
    /// value carries the identical expected value, otherwise <see cref="ConstraintVerdict.Undecided"/>; never Violated.
    /// </summary>
    internal static ConstraintVerdict DecideEquality(EqualityConstraint target, AbstractValue value) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);

        return value.Constraints.OfType<EqualityConstraint>().Any(e => Equals(e.ExpectedValue, target.ExpectedValue))
            ? ConstraintVerdict.Satisfied
            : ConstraintVerdict.Undecided;
    }

    /// <summary>
    /// A target bound no exact number can order. A numeric bound is always orderable (the compare
    /// falls back to master's double comparison); only a non-numeric bound is unorderable.
    /// Shared with the effect analyzer's range text so the two cannot disagree.
    /// </summary>
    internal static bool HasUnorderableBound(RangeConstraint range) =>
        (range.Minimum is not null && !ConstraintMerge.IsNumeric(range.Minimum))
        || (range.Maximum is not null && !ConstraintMerge.IsNumeric(range.Maximum));
}

/// <summary>The three-way outcome of one write: proven inside, proven outside, or not proven.</summary>
internal enum WriteOutcome { Satisfied, Violated, Unverified }

/// <summary>
/// One write's outcome under the four-way rule (D2): Satisfied (silent, counted), Violated (DMEFF008
/// error), or Unverified with the reason that splits the last two outcomes (a known range that can
/// fall outside is a DMEFF008 warning; anything else is DMCS003).
/// <para>
/// A verdict that reports a finding also carries the diagnostic <see cref="Code"/> it was reported
/// under. The code is the arm's decision, not a function of the reason: the parameter arm reports
/// <see cref="UnverifiedReason.UnknownBound"/> as a <c>DMEFF008</c> warning while the assign arm
/// reports it as <c>DMCS003</c> (V-04 fix round 2, G1). <see cref="Satisfied"/> carries none and
/// never becomes a fact.
/// </para>
/// </summary>
internal readonly record struct WriteVerdict(WriteOutcome Outcome, UnverifiedReason? Reason, string? Code) {
    /// <summary>The write is proven inside every stage context's constraint.</summary>
    public bool IsVerified => Outcome == WriteOutcome.Satisfied;

    public static WriteVerdict Satisfied => new(WriteOutcome.Satisfied, null, null);

    /// <summary>The write is proven entirely outside its constraint.</summary>
    public static WriteVerdict Violated =>
        new(WriteOutcome.Violated, null, DomainModelDiagnosticCodes.EffectConstraintViolation);

    /// <summary>The write could not be proven inside its constraint, for the given reason and code.</summary>
    public static WriteVerdict Unverified(UnverifiedReason reason, string code) =>
        new(WriteOutcome.Unverified, reason, code);

    private int Rank => Outcome switch {
        WriteOutcome.Violated => 100,
        WriteOutcome.Unverified => 10 + ReasonRank,
        _ => 0,
    };

    /// <summary>
    /// Orders the Unverified reasons so the worst one wins deterministically rather than by the order
    /// stage contexts were analyzed in (review H4). A known range that can fall outside is the least
    /// unknown; a cut recursion is the most.
    /// </summary>
    private int ReasonRank => Reason switch {
        UnverifiedReason.RecursionCut => 4,
        UnverifiedReason.NonFlowCheckedKind => 3,
        UnverifiedReason.UnsupportedOperator => 2,
        UnverifiedReason.UnknownBound => 1,
        UnverifiedReason.PossibleViolation => 0,
        null => 0, // the Satisfied and Violated verdicts carry no reason.
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "a new UnverifiedReason must name its rank"),
    };

    /// <summary>The worse of two verdicts (Violated, then Unverified, then Satisfied); the first wins a tie.</summary>
    public WriteVerdict Worst(WriteVerdict other) => other.Rank > Rank ? other : this;

    /// <summary>
    /// The rank of one Unverified reason (the tally's ordering), so a reader outside the tally can
    /// keep the worst of several facts deterministically. Higher is worse.
    /// </summary>
    internal static int RankFor(UnverifiedReason? reason) =>
        new WriteVerdict(WriteOutcome.Unverified, reason, null).ReasonRank;
}

/// <summary>
/// Why a write could not be verified (the fourth outcome's discriminator, D2). A definite
/// violation stays a separate DMEFF008 error and is recorded in
/// <c>WriteVerificationFacts.Violations</c> (internal; the public surface exposes the reason only).
/// </summary>
public enum UnverifiedReason {
    /// <summary>A known value range overlaps the constraint: it can fall outside (DMEFF008 warning).</summary>
    PossibleViolation,

    /// <summary>The value has no usable numeric bound (Unknown, or an open side against a closed target).</summary>
    UnknownBound,

    /// <summary>The RHS is an operator analysis does not model (division, call, match, ...).</summary>
    UnsupportedOperator,

    /// <summary>The written property's constraint kind has no flow decision (fail-closed kind default).</summary>
    NonFlowCheckedKind,

    /// <summary>
    /// The value came from an <c>invoke</c> already on the call stack, so its postcondition was cut.
    /// Nothing produces this yet: the recorder must not invent a verdict for a callee write it did
    /// not see, and the cut itself is V-09's diagnostic.
    /// </summary>
    RecursionCut,
}

/// <summary>
/// Prose for an unverified write's reason, shared by the diagnostics that report one and the
/// summary's synthesised rows, so a row never shows a bare enum name (V-04 fix round 2, G2).
/// </summary>
internal static class UnverifiedReasonText {
    public static string Of(UnverifiedReason? reason) => reason switch {
        UnverifiedReason.PossibleViolation => "the value can fall outside the constraint",
        UnverifiedReason.UnknownBound => "the assigned value has no bound analysis can use",
        UnverifiedReason.UnsupportedOperator => "the expression uses an operator analysis does not model",
        UnverifiedReason.NonFlowCheckedKind => "the constraint kind is not checked against computed values",
        UnverifiedReason.RecursionCut => "the value comes from a recursive invoke whose effect was cut",
        _ => "the write could not be verified",
    };
}

/// <summary>
/// One write analysis could not verify, or proved outside its constraint, as recorded in
/// <see cref="WriteVerificationFacts"/>. <see cref="Reason"/> is null for a proven violation.
/// <see cref="ReportedCode"/> is the diagnostic code the reporting arm emitted, stored rather than
/// derived so a row can be matched to its own fact (V-04 fix round 2, G1).
/// </summary>
internal sealed record UnverifiedWrite(
    Node Effect,
    string TargetProperty,
    string Constraint,
    UnverifiedReason? Reason,
    string ReportedCode);

/// <summary>
/// The analysis-side record of every constrained write in one domain (V-03 / A2-A4), keyed on the
/// <see cref="Domain"/> node — the V-04 <c>IAnalysisMetadata</c> contract. A write is verified only
/// when every stage context is Satisfied, so <see cref="Unverified"/> holds one entry per write that
/// was not; V-04 must never report "proven" while it is non-empty.
/// </summary>
internal sealed class WriteVerificationFacts : IAnalysisMetadata {
    /// <summary>Writes that could not be proven inside their constraint, with the reason.</summary>
    public List<UnverifiedWrite> Unverified { get; } = [];

    /// <summary>Writes proven entirely outside their constraint (the definite DMEFF008 errors).</summary>
    public List<UnverifiedWrite> Violations { get; } = [];

    /// <summary>Count of writes proven inside every stage context's constraint.</summary>
    public int Verified { get; private set; }

    public void RecordVerified() => Verified++;

    public void RecordUnverified(UnverifiedWrite write) => Unverified.Add(write);

    public void RecordViolation(UnverifiedWrite write) => Violations.Add(write);
}

/// <summary>
/// The one recorder (A3): the literal path, the parameter arm and the assign channel all end here,
/// writing ONE fact per write (a write with several constraints is still one write) so V-04 can
/// count writes, never constraints.
/// </summary>
internal static class WriteRecorder {
    internal static void Record(
        AnalysisContext context, Domain domain, Node effect, string property, string constraint, WriteVerdict verdict) {
        var facts = WriteVerificationFactsStore.For(context, domain);
        switch (verdict.Outcome) {
            case WriteOutcome.Satisfied:
                facts.RecordVerified();
                break;
            case WriteOutcome.Violated:
                facts.RecordViolation(new UnverifiedWrite(effect, property, constraint, null, verdict.Code!));
                break;
            default:
                facts.RecordUnverified(new UnverifiedWrite(
                    effect, property, constraint, verdict.Reason ?? UnverifiedReason.UnknownBound, verdict.Code!));
                break;
        }
    }
}

/// <summary>
/// Accumulates the verdicts of one write's constraints (the literal and parameter arms decide per
/// constraint) into the single verdict <see cref="WriteRecorder"/> records for the write.
/// </summary>
internal struct WriteTally {
    private WriteVerdict _verdict;
    private string? _constraint;
    private bool _any;

    public void Add(WriteVerdict verdict, Constraint constraint) {
        var worst = _verdict.Worst(verdict);
        if (!_any || worst != _verdict) {
            _verdict = worst;
            _constraint = ConstraintValidation.Describe(constraint);
        }
        _any = true;
    }

    /// <summary>Records the write once, when at least one constraint was decided for it.</summary>
    public readonly void Record(AnalysisContext context, Domain domain, Node effect, string property) {
        if (_any) WriteRecorder.Record(context, domain, effect, property, _constraint ?? "", _verdict);
    }
}

/// <summary>The domain-keyed facts store: one bucket per <see cref="Domain"/> for one analysis run.</summary>
internal static class WriteVerificationFactsStore {
    /// <summary>
    /// The facts for <paramref name="domain"/>, created on first use. The domain pass creates it
    /// before anything else, so after a full analysis a missing bucket means the pass did not run.
    /// </summary>
    internal static WriteVerificationFacts For(AnalysisContext context, Domain domain) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(domain);
        return context.GetOrAddMetadata(domain, static () => new WriteVerificationFacts());
    }
}