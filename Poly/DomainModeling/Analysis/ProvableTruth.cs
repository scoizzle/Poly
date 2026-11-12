using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

/// <summary>The three answers a provable check can give (public per B6; V-10 and T-12 consume it).</summary>
public enum Truth {
    /// <summary>Every value the abstract environment allows satisfies the condition.</summary>
    True,

    /// <summary>No value the abstract environment allows satisfies the condition.</summary>
    False,

    /// <summary>The environment does not decide it. Never read as either answer.</summary>
    Unknown,
}

/// <summary>
/// The three-valued truth of a boolean condition over abstract values, decided from their intervals.
/// A leaf the flow does not model — an existential, a quantifier, a relationship — is
/// <see cref="Truth.Unknown"/> (fail closed), never a guess. The comparison arms read the same
/// <see cref="IntervalTransfers"/> table the evaluator uses, so there is one arithmetic meaning (A4).
/// </summary>
internal static class ProvableTruth {
    /// <summary>The truth of <paramref name="condition"/> under <paramref name="env"/>.</summary>
    internal static Truth Decide(DomainExpression condition, IReadOnlyDictionary<string, AbstractValue> env) {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(env);
        return condition switch {
            And and => Conjunction(Decide(and.Left, env), Decide(and.Right, env)),
            Or or => Disjunction(Decide(or.Left, env), Decide(or.Right, env)),
            Not not => Negation(Decide(not.Operand, env)),
            Comparison comparison => Compare(comparison, env),
            _ => Truth.Unknown,
        };
    }

    /// <summary>Kleene conjunction: false wins, true only when both are true, else unknown.</summary>
    private static Truth Conjunction(Truth left, Truth right) =>
        left == Truth.False || right == Truth.False ? Truth.False
        : left == Truth.True && right == Truth.True ? Truth.True
        : Truth.Unknown;

    /// <summary>Kleene disjunction: true wins, false only when both are false, else unknown.</summary>
    private static Truth Disjunction(Truth left, Truth right) =>
        left == Truth.True || right == Truth.True ? Truth.True
        : left == Truth.False && right == Truth.False ? Truth.False
        : Truth.Unknown;

    private static Truth Negation(Truth truth) => truth switch {
        Truth.True => Truth.False,
        Truth.False => Truth.True,
        _ => Truth.Unknown,
    };

    private static Truth Compare(Comparison comparison, IReadOnlyDictionary<string, AbstractValue> env) {
        var left = Interval(comparison.Left, env);
        var right = Interval(comparison.Right, env);
        if (left is not { } l || right is not { } r) return Truth.Unknown;

        if (comparison.Kind is ComparisonKind.Equal or ComparisonKind.NotEqual) {
            var equal = EqualTruth(l, r);
            return comparison.Kind == ComparisonKind.Equal ? equal : Negation(equal);
        }

        // An order is decided only against the pair of endpoints that proves it: the two ranges are
        // separate when one wholly precedes the other, and incomparable otherwise.
        var (alwaysTrue, neverTrue) = comparison.Kind switch {
            ComparisonKind.LessThan => (
                l.Max is { } lmax && r.Min is { } rmin && lmax < rmin,
                l.Min is { } lmin && r.Max is { } rmax && lmin >= rmax),
            ComparisonKind.LessThanOrEqual => (
                l.Max is { } lmax2 && r.Min is { } rmin2 && lmax2 <= rmin2,
                l.Min is { } lmin2 && r.Max is { } rmax2 && lmin2 > rmax2),
            ComparisonKind.GreaterThan => (
                l.Min is { } lmin3 && r.Max is { } rmax3 && lmin3 > rmax3,
                l.Max is { } lmax3 && r.Min is { } rmin3 && lmax3 <= rmin3),
            ComparisonKind.GreaterThanOrEqual => (
                l.Min is { } lmin4 && r.Max is { } rmax4 && lmin4 >= rmax4,
                l.Max is { } lmax4 && r.Min is { } rmin4 && lmax4 < rmin4),
            _ => (false, false),
        };
        return alwaysTrue ? Truth.True : neverTrue ? Truth.False : Truth.Unknown;
    }

    /// <summary>Two ranges are equal for every value only when both are the same single point; they are
    /// unequal for every value when they are disjoint. Anything else is unknown.</summary>
    private static Truth EqualTruth(ValueRange left, ValueRange right) {
        if (left.Min is { } lmin && left.Max is { } lmax && lmin == lmax
            && right.Min == lmin && right.Max == lmax)
            return Truth.True;
        if (left.Max is { } lmax2 && right.Min is { } rmin && lmax2 < rmin) return Truth.False;
        if (right.Max is { } rmax && left.Min is { } lmin2 && rmax < lmin2) return Truth.False;
        return Truth.Unknown;
    }

    /// <summary>An expression's interval under an env-only read: a property/parameter interval from the
    /// environment, a numeric literal's exact point, and an operator through the transfer table. Every
    /// other leaf is null (unknown) — never a guess (A4).</summary>
    private static ValueRange? Interval(DomainExpression expression, IReadOnlyDictionary<string, AbstractValue> env) {
        switch (expression) {
            case PropertyAccess property:
                return env.TryGetValue(property.Name, out var propertyValue) ? propertyValue.NumericRange : null;
            case ParameterAccess parameter:
                return env.TryGetValue(parameter.Name, out var parameterValue) ? parameterValue.NumericRange : null;
            case Literal { Value: { } literal } when ExactNumber.TryFrom(literal)?.ToDecimal() is { } exact:
                return new ValueRange(exact, exact);
            case Add add:
                return IntervalTransfers.Transfer(add, [Interval(add.Left, env), Interval(add.Right, env)]);
            case Subtract sub:
                return IntervalTransfers.Transfer(sub, [Interval(sub.Left, env), Interval(sub.Right, env)]);
            case Multiply mul:
                return IntervalTransfers.Transfer(mul, [Interval(mul.Left, env), Interval(mul.Right, env)]);
            default:
                return null;
        }
    }
}