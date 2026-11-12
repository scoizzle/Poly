using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The abstract value of a symbol (property, parameter, binder) or expression result:
/// a merged constraint set — one constraint per type (several only for a conjunction such as two patterns), combined by intersection
/// (via <see cref="ConstraintMerge"/>). This is the single representation underlying
/// domain propagation: preconditions refine it, expressions compose it, and postconditions
/// verify it against declared constraints. Replaces the numeric-only value range.
/// </summary>
public sealed class AbstractValue {
    private readonly IReadOnlyList<Constraint> _constraints;

    /// <summary>No static knowledge — the value is unconstrained (top).</summary>
    public static readonly AbstractValue Unknown = new([]);

    private AbstractValue(IReadOnlyList<Constraint> constraints, bool unsatisfiable = false) {
        _constraints = constraints;
        Unsatisfiable = unsatisfiable;
    }

    /// <summary>The merged constraints (one per type), or empty for <see cref="Unknown"/>.</summary>
    public IReadOnlyList<Constraint> Constraints => _constraints;

    /// <summary>True when the constraints' intersection is empty (e.g. two disjoint ranges)
    /// — the value cannot exist; a precondition that yields this is unsatisfiable.</summary>
    public bool Unsatisfiable { get; }

    /// <summary>Builds an abstract value by merging same-type constraints; marks unsatisfiable
    /// when a same-type merge is empty. A merge that cannot decide (unrepresentable, or a kind
    /// with no rule) drops that axis rather than pretending it is empty.</summary>
    public static AbstractValue From(IEnumerable<Constraint> constraints) {
        var merged = new List<Constraint>();
        var unsatisfiable = false;
        var dropped = new HashSet<Type>();
        foreach (var c in constraints) {
            if (dropped.Contains(c.GetType()))
                continue;
            switch (Absorb(merged, c)) {
                case Absorbed.Empty:
                    unsatisfiable = true;
                    break;
                case Absorbed.Dropped:
                    dropped.Add(c.GetType());
                    break;
            }
        }
        return merged.Count == 0 && !unsatisfiable ? Unknown : new AbstractValue(merged, unsatisfiable);
    }

    private enum Absorbed { Added, Empty, Dropped }

    /// <summary>
    /// Adds <paramref name="c"/> to the conjunction in <paramref name="list"/>: merged into a
    /// same-type entry when they intersect to one constraint, kept beside it when both must hold
    /// (<see cref="ConstraintMerge.Coexists"/>), otherwise the whole axis is removed (widened).
    /// </summary>
    private static Absorbed Absorb(List<Constraint> list, Constraint c) {
        var coexist = true;
        for (var i = 0; i < list.Count; i++) {
            if (list[i].GetType() != c.GetType()) continue;
            var merge = list[i].MergeResult(c);
            switch (merge.Kind) {
                case ConstraintMergeKind.Net:
                    list[i] = merge.Net!;
                    return Absorbed.Added;
                case ConstraintMergeKind.Empty:
                    return Absorbed.Empty;
                default:
                    if (!ConstraintMerge.Coexists(list[i], c)) coexist = false;
                    break;
            }
        }
        if (coexist) {
            list.Add(c);
            return Absorbed.Added;
        }
        var type = c.GetType();
        list.RemoveAll(m => m.GetType() == type);
        return Absorbed.Dropped;
    }

    /// <summary>Intersection of two abstract values (per type).</summary>
    public AbstractValue Merge(AbstractValue other) {
        var merged = MergeCore(other);
        // Bottom is sticky: intersecting with a value that has no admissible value cannot make one appear.
        return (Unsatisfiable || other.Unsatisfiable) && !merged.Unsatisfiable ? merged.AsUnsatisfiable() : merged;
    }

    private AbstractValue MergeCore(AbstractValue other) {
        if (ReferenceEquals(this, Unknown) || _constraints.Count == 0) return other;
        if (other.Constraints.Count == 0) return this;
        return From(_constraints.Concat(other.Constraints));
    }

    /// <summary>
    /// JOIN (least upper bound) of two abstract values: what holds whenever EITHER holds. Unlike
    /// <see cref="Merge"/> (intersection / meet), a fact survives only if both values carry it,
    /// widened per <see cref="ConstraintMerge.Join"/>; an axis missing on either side is dropped.
    /// An unsatisfiable value is bottom and is the identity (the arm cannot happen); a value with
    /// no constraints is top and absorbs everything.
    /// </summary>
    public AbstractValue Join(AbstractValue other) {
        ArgumentNullException.ThrowIfNull(other);
        if (Unsatisfiable) return other;
        if (other.Unsatisfiable) return this;
        var joined = new List<Constraint>();
        foreach (var c in _constraints) {
            foreach (var o in other.Constraints) {
                if (o.GetType() != c.GetType()) continue;
                if (c.Join(o) is { } j && !joined.Any(x => ConstraintMerge.Identical(x, j)))
                    joined.Add(j);
            }
        }
        return joined.Count == 0 ? Unknown : new AbstractValue(joined);
    }

    /// <summary>Applies a single extra constraint (a bound from a guard/if/where condition).</summary>
    public AbstractValue Narrow(Constraint constraint) {
        var list = _constraints.ToList();
        AbstractValue narrowed;
        if (!list.Any(c => c.GetType() == constraint.GetType()))
            narrowed = From([.. list, constraint]);
        else
            narrowed = Absorb(list, constraint) == Absorbed.Empty
                ? new AbstractValue(list, unsatisfiable: true)
                : new AbstractValue(list);
        // Bottom is sticky: narrowing a value with no admissible value cannot make one appear.
        return Unsatisfiable && !narrowed.Unsatisfiable ? narrowed.AsUnsatisfiable() : narrowed;
    }

    /// <summary>This value with every relational constraint removed. Relations are facts about a
    /// symbol in the environment, not about a value, so they never travel with a read value.</summary>
    internal AbstractValue WithoutRelations() =>
        _constraints.Any(c => c is RelationalConstraint)
            ? new AbstractValue([.. _constraints.Where(c => c is not RelationalConstraint)], Unsatisfiable)
            : this;

    /// <summary>This value with every relation to <paramref name="property"/> removed (the property was
    /// reassigned, so "this vs property" is stale). Widening, hence sound.</summary>
    internal AbstractValue WithoutRelationsTo(string property) =>
        _constraints.Any(c => c is RelationalConstraint r && string.Equals(r.Other, property, StringComparison.Ordinal))
            ? new AbstractValue(
                [.. _constraints.Where(c => c is not RelationalConstraint r || !string.Equals(r.Other, property, StringComparison.Ordinal))],
                Unsatisfiable)
            : this;

    /// <summary>The same constraints, marked as having no admissible value (bottom).</summary>
    internal AbstractValue AsUnsatisfiable() => new(_constraints, unsatisfiable: true);

    /// <summary>The RangeConstraint, if the value carries one.</summary>
    public RangeConstraint? Range => _constraints.OfType<RangeConstraint>().FirstOrDefault();

    /// <summary>The numeric bounds as a <see cref="ValueRange"/>, when the value carries a range.</summary>
    public ValueRange? NumericRange {
        get {
            var range = Range;
            if (range is null) return null;
            var min = NumericBound.ToDoubleOrNull(range.Minimum);
            var max = NumericBound.ToDoubleOrNull(range.Maximum);
            return min is null && max is null ? null : new ValueRange(min, max);
        }
    }
}