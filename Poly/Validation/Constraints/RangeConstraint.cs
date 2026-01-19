using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using static Poly.Interpretation.AbstractSyntaxTree.NodeExtensions;

namespace Poly.Validation;

public sealed class RangeConstraint(object? minValue, object? maxValue) : Constraint {
    public object? MinValue { get; set; } = minValue;
    public object? MaxValue { get; set; } = maxValue;

    public override Node BuildInterpretationTree(RuleBuildingContext context)
    {
        var member = context.Value;

        Node? minCheck = MinValue is null
            ? null
            : new GreaterThanOrEqual(member, Wrap(MinValue));

        Node? maxCheck = MaxValue is null
            ? null
            : new LessThanOrEqual(member, Wrap(MaxValue));

        var rangeCheck = (minCheck, maxCheck) switch {
            (Node min, Node max) => new And(min, max),
            (Node min, null) => min,
            (null, Node max) => max,
            _ => True
        };

        return rangeCheck;
    }

    public override string ToString() => (MinValue, MaxValue) switch {
        (not null, not null) => $"value >= {MinValue} and value <= {MaxValue}",
        (not null, null) => $"value >= {MinValue}",
        (null, not null) => $"value <= {MaxValue}",
        (null, null) => "no range constraints"
    };
}