using Poly.Interpretation;
using Poly.Interpretation.Expressions;

namespace Poly.Validation;

public sealed class RangeConstraint(object? minValue, object? maxValue) : Constraint {
    public object? MinValue { get; set; } = minValue;
    public object? MaxValue { get; set; } = maxValue;

    public override Interpretable BuildInterpretationTree(RuleBuildingContext context)
    {
        var member = context.Value;

        Interpretable? minCheck = MinValue is null
            ? null
            : new BinaryOperation(BinaryOperationKind.GreaterThanOrEqual, member, new Constant(MinValue));

        Interpretable? maxCheck = MaxValue is null
            ? null
            : new BinaryOperation(BinaryOperationKind.LessThanOrEqual, member, new Constant(MaxValue));

        var rangeCheck = (minCheck, maxCheck) switch {
            (Interpretable min, Interpretable max) => new BinaryOperation(BinaryOperationKind.And, min, max),
            (Interpretable min, null) => min,
            (null, Interpretable max) => max,
            _ => new Constant(true)
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