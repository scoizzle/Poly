using Poly.Interpretation;
using Poly.Interpretation.Operators.Equality;

namespace Poly.StateManagement.Constraints;

public sealed record EqualityConstraint(string propertyName, object value) : Constraint(propertyName)
{
    public object Value { get; set; } = value;

    public override Value BuildInterpretationTree(RuleInterpretationContext context) {
        var member = context.GetMemberAccessor(PropertyName);
        var valueLiteral = new Literal(Value);
        return new Equal(member, valueLiteral);
    }

    public override string ToString() => $"{PropertyName} == {Value}";
}
