using Poly.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// V-03 (Q4): the unverified-write channel's shared vocabulary and its one range decision
/// procedure. <see cref="DecideRange"/> is consumed by <c>EffectAnalyzer.CheckDerivedRange</c>
/// and, once the assign gate widens (S3), by every constrained write; it reads absent
/// endpoints explicitly (a null side on the target is satisfied, a null side on the value
/// against a closed target is Undecided, never Violated).
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

    private static bool HasUnorderableBound(RangeConstraint range) =>
        (range.Minimum is not null && NumericBound.ToDoubleOrNull(range.Minimum) is null)
        || (range.Maximum is not null && NumericBound.ToDoubleOrNull(range.Maximum) is null);
}

/// <summary>One write's outcome under the four-way rule (D2).</summary>
internal readonly record struct WriteVerdict(bool IsVerified, UnverifiedReason? Reason) {
    /// <summary>The write is proven inside every stage context's constraint.</summary>
    public static WriteVerdict Satisfied => new(true, null);

    /// <summary>The write could not be proven inside its constraint, for the given reason.</summary>
    public static WriteVerdict Unverified(UnverifiedReason reason) => new(false, reason);
}

/// <summary>
/// Why a write could not be verified (the fourth outcome's discriminator, D2). A definite
/// violation stays a separate DMEFF008 error and is recorded in
/// <see cref="WriteVerificationFacts.Violations"/>.
/// </summary>
internal enum UnverifiedReason {
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

/// <summary>One write analysis could not verify, as recorded in <see cref="WriteVerificationFacts"/>.</summary>
internal sealed record UnverifiedWrite(
    Node Effect,
    string TargetProperty,
    string Constraint,
    UnverifiedReason Reason);

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

/// <summary>The domain-keyed facts store: one bucket per <see cref="Domain"/> for one analysis run.</summary>
internal static class WriteVerificationFactsStore {
    /// <summary>The facts for <paramref name="domain"/>, created on the first recorded write.</summary>
    internal static WriteVerificationFacts For(AnalysisContext context, Domain domain) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(domain);
        return context.GetOrAddMetadata(domain, static () => new WriteVerificationFacts());
    }
}