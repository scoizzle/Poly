using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;
using Poly.Interpretation.Operators.Comparison;

namespace Poly.StateManagement.Constraints;

public sealed class MinValueConstraint(string memberName, object minValue) : Constraint(memberName)
{
    public object MinValue { get; set; } = minValue;

    public override Value BuildInterpretationTree(RuleInterpretationContext context) {
        Literal minValueLiteral = new(MinValue);
        Value memberValue = context.GetMemberAccessor(PropertyName);
        return new GreaterThanOrEqual(memberValue, minValueLiteral);

    }

    public override string ToString() => $"{PropertyName} >= {MinValue}";
}