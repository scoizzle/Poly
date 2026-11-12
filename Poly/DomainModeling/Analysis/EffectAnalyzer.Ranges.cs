// EffectAnalyzer partial: derived value range checks.
using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

internal sealed partial class EffectAnalyzer {
    /// <summary>
    /// Constraint propagation onto derived (arithmetic) values: infer the value range the
    /// RHS expression can produce (from literals, property/param ranges, and arithmetic
    /// composition) and check it against the target property's RangeConstraint. A range
    /// entirely outside the constraint is a definite violation (error); a range that can
    /// extend outside is a possible violation (warning).
    /// </summary>
    private static void ValidateDerivedValueRange(
        AnalysisContext context,
        Node errorNode,
        DomainExpression value,
        Entity entity,
        Action? action,
        Property targetProp) {
        var range = targetProp.Constraints.OfType<RangeConstraint>().FirstOrDefault();
        if (range is null) return;

        var meta = action is null ? null : context.GetMetadata<ActionInvariantMetadata>(action);
        if (meta is null) {
            // No action context (create-initializer): infer against declared ranges only.
            var (lo, hi) = EffectInvariantAnalyzer.InferNumericRange(value, entity, null, null);
            CheckDerivedRange(context, errorNode, AbstractValue.From([new RangeConstraint(lo, hi)]), range, targetProp);
            return;
        }

        // The effect is valid in one or more stage contexts (entity states); check the
        // postcondition in each — a violation in ANY state the action can run is reported.
        foreach (var ctx in meta.StageContexts) {
            var post = ctx.Postconditions.FirstOrDefault(p => ReferenceEquals(p.Effect, errorNode));
            if (post is not null)
                CheckDerivedRange(context, errorNode, post.Value, range, targetProp);
        }
    }

    private static void CheckDerivedRange(
        AnalysisContext context,
        Node errorNode,
        AbstractValue value,
        RangeConstraint range,
        Property targetProp) {
        // No numeric range at all is "Unknown", not the open interval (-∞, +∞): the
        // create-initializer callers reach here through InferNumericRange for every unranged
        // arithmetic initializer. Treating it as open would warn each one against its target.
        // (S3 removes this skip and records the write as Undecided.)
        if (value.NumericRange is not { } numeric) return;

        // The null-endpoint semantics live in WriteVerification.DecideRange: a null target side
        // is satisfied, a null value side against a closed target is Undecided, never Violated.
        switch (WriteVerification.DecideRange(range, value)) {
            case ConstraintVerdict.Violated:
                context.ReportError(
                    errorNode,
                    $"Assigned expression value range [{FormatBound(numeric.Min, lower: true)}, {FormatBound(numeric.Max, lower: false)}] is entirely outside " +
                    $"constraint {ConstraintValidation.Describe(range)} on property '{targetProp.Name}'.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
                break;
            case ConstraintVerdict.Undecided:
                context.ReportWarning(
                    errorNode,
                    $"Assigned expression value range [{FormatBound(numeric.Min, lower: true)}, {FormatBound(numeric.Max, lower: false)}] can fall outside " +
                    $"constraint {ConstraintValidation.Describe(range)} on property '{targetProp.Name}'.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
                break;
            case ConstraintVerdict.Satisfied:
            default:
                break;
        }
    }

    private static bool IsRangeSubsumed(RangeConstraint inner, RangeConstraint outer) {
        var innerMin = inner.Minimum is not null ? NumericBound.ToDoubleOrNull(inner.Minimum) : double.NegativeInfinity;
        var innerMax = inner.Maximum is not null ? NumericBound.ToDoubleOrNull(inner.Maximum) : double.PositiveInfinity;
        var outerMin = outer.Minimum is not null ? NumericBound.ToDoubleOrNull(outer.Minimum) : double.NegativeInfinity;
        var outerMax = outer.Maximum is not null ? NumericBound.ToDoubleOrNull(outer.Maximum) : double.PositiveInfinity;

        if (innerMin is null || innerMax is null || outerMin is null || outerMax is null) return false;

        return innerMin.Value >= outerMin.Value && innerMax.Value <= outerMax.Value;
    }

    private static bool HasUnorderableBound(RangeConstraint range) =>
        (range.Minimum is not null && NumericBound.ToDoubleOrNull(range.Minimum) is null)
        || (range.Maximum is not null && NumericBound.ToDoubleOrNull(range.Maximum) is null);
}