using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The abstract value of a symbol (property, parameter, binder) or expression result:
/// a merged constraint set — at most one constraint per type, combined by intersection
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
    /// when a same-type merge returns null (empty intersection).</summary>
    public static AbstractValue From(IEnumerable<Constraint> constraints) {
        var merged = new List<Constraint>();
        var unsatisfiable = false;
        foreach (var c in constraints) {
            var idx = merged.FindIndex(m => m.GetType() == c.GetType());
            if (idx < 0) {
                merged.Add(c);
                continue;
            }
            if (merged[idx].Merge(c) is { } net) {
                merged[idx] = net;
            }
            else {
                unsatisfiable = true;
            }
        }
        return merged.Count == 0 && !unsatisfiable ? Unknown : new AbstractValue(merged, unsatisfiable);
    }

    /// <summary>Intersection of two abstract values (per type).</summary>
    public AbstractValue Merge(AbstractValue other) {
        if (ReferenceEquals(this, Unknown) || _constraints.Count == 0) return other;
        if (other.Constraints.Count == 0) return this;
        return From(_constraints.Concat(other.Constraints));
    }

    /// <summary>Applies a single extra constraint (a bound from a guard/if/where condition).</summary>
    public AbstractValue Narrow(Constraint constraint) {
        var list = _constraints.ToList();
        var idx = list.FindIndex(c => c.GetType() == constraint.GetType());
        if (idx < 0)
            return From([.. list, constraint]);
        if (list[idx].Merge(constraint) is { } net) {
            list[idx] = net;
            return new AbstractValue(list);
        }
        return new AbstractValue(list, unsatisfiable: true);
    }

    /// <summary>The RangeConstraint, if the value carries one.</summary>
    public RangeConstraint? Range => _constraints.OfType<RangeConstraint>().FirstOrDefault();

    /// <summary>The numeric bounds as a <see cref="ValueRange"/>, when the value carries a range.</summary>
    public ValueRange? NumericRange {
        get {
            var range = Range;
            if (range is null) return null;
            var min = ToDoubleOrNull(range.Minimum);
            var max = ToDoubleOrNull(range.Maximum);
            return min is null && max is null ? null : new ValueRange(min, max);
        }
    }

    private static double? ToDoubleOrNull(object? v) {
        try { return Convert.ToDouble(v); }
        catch { return null; }
    }
}