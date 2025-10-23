using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;
using Poly.Interpretation.Operators.Comparison;

namespace Poly.Validation;

public sealed class LengthConstraint(string propertyName, int? minLength, int? maxLength) : Constraint(propertyName)
{
    public int? MinLength { get; set; } = minLength;
    public int? MaxLength { get; set; } = maxLength;

    public override Value BuildInterpretationTree(RuleInterpretationContext context) {
        var member = context.GetMemberAccessor(PropertyName);
        var length = member.GetMember("Length");

        var minCheck = MinLength.HasValue
            ? new GreaterThanOrEqual(length, new Literal(MinLength.Value))
            : null;

        var maxCheck = MaxLength.HasValue
            ? new LessThanOrEqual(length, new Literal(MaxLength.Value))
            : null;

        return (minCheck, maxCheck) switch {
            (Value min, Value max) => new And(min, max),
            (Value min, null) => min,
            (null, Value max) => max,
            _ => new Literal(true)
        };
    }
    
    public override string ToString() {
        if (MinLength.HasValue && MaxLength.HasValue) {
            return $"{PropertyName}.Length >= {MinLength.Value} && {PropertyName}.Length <= {MaxLength.Value}";
        }
        else if (MinLength.HasValue) {
            return $"{PropertyName}.Length >= {MinLength.Value}";
        }
        else if (MaxLength.HasValue) {
            return $"{PropertyName}.Length <= {MaxLength.Value}";
        }
        else {
            return "true";
        }
    }
}
