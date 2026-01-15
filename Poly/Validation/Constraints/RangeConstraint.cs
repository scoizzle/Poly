using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;
using Poly.Interpretation.Operators.Comparison;

namespace Poly.Validation;

public sealed class RangeConstraint(object? minValue, object? maxValue) : Constraint {
    public object? MinValue { get; set; } = minValue;
    public object? MaxValue { get; set; } = maxValue;

    public override Value BuildInterpretationTree(RuleBuildingContext context)
    {
        var member = context.Value;

        Value? minCheck = MinValue is null
            ? null
            : new GreaterThanOrEqual(member, Value.Wrap(MinValue));

        Value? maxCheck = MaxValue is null
            ? null
            : new LessThanOrEqual(member, Value.Wrap(MaxValue));

        var rangeCheck = (minCheck, maxCheck) switch {
            (Value min, Value max) => new And(min, max),
            (Value min, null) => min,
            (null, Value max) => max,
            _ => Value.True
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