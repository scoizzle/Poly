using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;
using Poly.Interpretation.Operators.Comparison;

namespace Poly.StateManagement;

public sealed record LengthConstraint(string propertyName, int? minLength, int? maxLength) : Constraint(propertyName)
{
    public int? MinLength { get; init; } = minLength;
    public int? MaxLength { get; init; } = maxLength;

    public override Value BuildInterpretationTree(RuleInterpretationContext context) {
        var member = context.GetMemberAccessor(PropertyName);
        var length = member.GetMember("Length");

        var minCheck = MinLength.HasValue
            ? new GreaterThanOrEqual(length, new Literal(MinLength.Value))
            : null;

        var maxCheck = MaxLength.HasValue
            ? new LessThanOrEqual(length, new Literal(MaxLength.Value))
            : null;

        return (minCheck, maxCheck) switch
        {
            (Value min, Value max) => new And(min, max),
            (Value min, null) => min,
            (null, Value max) => max,
            _ => new Literal(true)
        };
    }
}
