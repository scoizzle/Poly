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
                    if (post.ValueRange is not { } vr) continue;
                    var declared = entity.Properties
                        .FirstOrDefault(p => string.Equals(p.Name, post.TargetProperty, StringComparison.Ordinal))
                        ?.Constraints.OfType<RangeConstraint>().FirstOrDefault();
                    if (declared is not null && !RangeWithin(vr, declared))
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
            if (declared is null || !BoundsOrderable(declared)) continue;
            result[prop.Name] = (ToValueRange(declared), true);
        }
        return result;
    }

    private static bool RangeWithin(ValueRange inner, RangeConstraint outer) {
        // #367: a bound that cannot be ordered is never skipped. A declared bound that does not convert,
        // or a written side that is unbounded against a declared bound, is not within.
        if (!BoundsOrderable(outer)) return false;
        var lo = ToDoubleOrNull(inner.Min);
        var hi = ToDoubleOrNull(inner.Max);
        if (lo is null && hi is null) return true;
        var dmin = ToDoubleOrNull(outer.Minimum);
        var dmax = ToDoubleOrNull(outer.Maximum);
        if (dmin is not null && lo is null) return false;
        if (dmax is not null && hi is null) return false;
        if (lo is not null && dmin is not null && lo < dmin) return false;
        if (hi is not null && dmax is not null && hi > dmax) return false;
        return true;
    }

    private static bool BoundsOrderable(RangeConstraint range) =>
        (range.Minimum is null || ToDoubleOrNull(range.Minimum) is not null)
        && (range.Maximum is null || ToDoubleOrNull(range.Maximum) is not null);

    private static ValueRange ToValueRange(RangeConstraint r) =>
        new(NumericBound.ToDecimalOrNull(r.Minimum), NumericBound.ToDecimalOrNull(r.Maximum));

    private static double? ToDoubleOrNull(object? v) {
        if (v is null) return null;
        try { return Convert.ToDouble(v); }
        catch { return null; }
    }
}