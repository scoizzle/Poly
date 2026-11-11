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
            CheckDerivedRange(context, errorNode, lo, hi, range, targetProp);
            return;
        }

        // The effect is valid in one or more stage contexts (entity states); check the
        // postcondition in each — a violation in ANY state the action can run is reported.
        foreach (var ctx in meta.StageContexts) {
            var post = ctx.Postconditions.FirstOrDefault(p => ReferenceEquals(p.Effect, errorNode));
            if (post?.ValueRange is { } vr)
                CheckDerivedRange(context, errorNode, vr.Min, vr.Max, range, targetProp);
        }
    }

    private static void CheckDerivedRange(
        AnalysisContext context,
        Node errorNode,
        double? loNullable,
        double? hiNullable,
        RangeConstraint range,
        Property targetProp) {
        if (loNullable is null || hiNullable is null) return;
        double lo = loNullable.Value, hi = hiNullable.Value;

        var tmin = ToDouble(range.Minimum);
        var tmax = ToDouble(range.Maximum);

        bool fullyBelow = tmin is not null && hi < tmin.Value;
        bool fullyAbove = tmax is not null && lo > tmax.Value;
        // #367: a target bound that cannot be ordered against a number is not skipped; nothing numeric is in it.
        if (fullyBelow || fullyAbove || HasUnorderableBound(range)) {
            context.ReportError(
                errorNode,
                $"Assigned expression value range [{FormatRangeValue(lo)}, {FormatRangeValue(hi)}] is entirely outside " +
                $"constraint {ConstraintValidation.Describe(range)} on property '{targetProp.Name}'.",
                DomainModelDiagnosticCodes.EffectConstraintViolation);
            return;
        }

        bool canViolate = (tmin is not null && lo < tmin.Value)
                          || (tmax is not null && hi > tmax.Value);
        if (canViolate) {
            context.ReportWarning(
                errorNode,
                $"Assigned expression value range [{FormatRangeValue(lo)}, {FormatRangeValue(hi)}] can fall outside " +
                $"constraint {ConstraintValidation.Describe(range)} on property '{targetProp.Name}'.",
                DomainModelDiagnosticCodes.EffectConstraintViolation);
        }
    }

    private static bool IsRangeSubsumed(RangeConstraint inner, RangeConstraint outer) {
        var innerMin = inner.Minimum is not null ? ToDouble(inner.Minimum) : double.NegativeInfinity;
        var innerMax = inner.Maximum is not null ? ToDouble(inner.Maximum) : double.PositiveInfinity;
        var outerMin = outer.Minimum is not null ? ToDouble(outer.Minimum) : double.NegativeInfinity;
        var outerMax = outer.Maximum is not null ? ToDouble(outer.Maximum) : double.PositiveInfinity;

        if (innerMin is null || innerMax is null || outerMin is null || outerMax is null) return false;

        return innerMin.Value >= outerMin.Value && innerMax.Value <= outerMax.Value;
    }

    private static bool HasUnorderableBound(RangeConstraint range) =>
        (range.Minimum is not null && ToDouble(range.Minimum) is null)
        || (range.Maximum is not null && ToDouble(range.Maximum) is null);
}