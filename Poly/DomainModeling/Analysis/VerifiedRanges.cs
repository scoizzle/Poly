using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The analysis-verified value envelope per property: for each writer (every action's
/// postconditions, across all stage contexts), checks that the postcondition range stays within the
/// property's declared <see cref="RangeConstraint"/>. A property is <b>verified</b> when no writer can
/// produce an out-of-range value — storage may then emit a CHECK constraint from the declared range
/// soundly, and the action-parameter envelope may use it. A property with any violating writer is
/// <b>not</b> verified (a CHECK would false-positive).
/// </summary>
internal static class VerifiedRanges {
    internal static Dictionary<string, (ValueRange? Range, bool Verified)> Compute(
        Entity entity,
        Func<Action, ActionInvariantMetadata?> invariants) {
        var violated = new HashSet<string>(StringComparer.Ordinal);

        void Scan(Action action) {
            var meta = invariants(action);
            if (meta is null) return;
            foreach (var stageCtx in meta.StageContexts) {
                foreach (var post in stageCtx.Postconditions) {
                    var declared = entity.Properties
                        .FirstOrDefault(p => string.Equals(p.Name, post.TargetProperty, StringComparison.Ordinal))
                        ?.Constraints.OfType<RangeConstraint>().FirstOrDefault();
                    if (declared is null) continue;
                    // A writer with no range is a violating writer (V-05b S3/A6): an unbounded write can
                    // produce any value, so a CHECK built from the declared range would false-reject.
                    // Both-open carries no information and reads the same as absent (A3).
                    if (post.ValueRange?.ToRangeConstraint() is not { } written
                        || !RangeWithin(written, declared))
                        violated.Add(post.TargetProperty);
                }
            }
        }

        foreach (var action in entity.Actions) Scan(action);
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions) Scan(action);

        var result = new Dictionary<string, (ValueRange?, bool)>(StringComparer.Ordinal);
        foreach (var prop in entity.Properties) {
            if (violated.Contains(prop.Name)) continue;
            var declared = prop.Constraints.OfType<RangeConstraint>().FirstOrDefault();
            if (declared is null || WriteVerification.HasUnorderableBound(declared)) continue;
            result[prop.Name] = (ToValueRange(declared), true);
        }
        return result;
    }

    private static bool RangeWithin(RangeConstraint inner, RangeConstraint outer) =>
        WriteVerification.DecideRange(outer, AbstractValue.From([inner])) == ConstraintVerdict.Satisfied;

    private static ValueRange ToValueRange(RangeConstraint r) =>
        new(ExactNumber.TryFrom(r.Minimum)?.ToDecimal(), ExactNumber.TryFrom(r.Maximum)?.ToDecimal());
}