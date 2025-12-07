using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;
using Poly.Interpretation.Operators.Comparison;

namespace Poly.Validation;

public sealed class LengthConstraint(int? minLength, int? maxLength) : Constraint {
    public int? MinLength { get; set; } = minLength;
    public int? MaxLength { get; set; } = maxLength;

    public override Value BuildInterpretationTree(RuleBuildingContext context) {
        var length = context.Value.GetMember("Length");

        var minCheck = MinLength.HasValue
            ? new GreaterThanOrEqual(length, new Literal(MinLength.Value))
            : null;

        var maxCheck = MaxLength.HasValue
            ? new LessThanOrEqual(length, new Literal(MaxLength.Value))
            : null;

        var lengthCheck = (minCheck, maxCheck) switch {
            (Value min, Value max) => new And(min, max),
            (Value min, null) => min,
            (null, Value max) => max,
            _ => new Literal(true)
        };

        return lengthCheck;
    }

    public override string ToString() {
        if (MinLength.HasValue && MaxLength.HasValue) {
            return $"value.Length >= {MinLength.Value} && value.Length <= {MaxLength.Value}";
        }
        else if (MinLength.HasValue) {
            return $"value.Length >= {MinLength.Value}";
        }
        else if (MaxLength.HasValue) {
            return $"value.Length <= {MaxLength.Value}";
        }
        else {
            return "true";
        }
    }
}