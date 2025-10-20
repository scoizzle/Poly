using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;
using Poly.Interpretation.Operators.Equality;

namespace Poly.StateManagement;

public sealed record NotNullConstraint(string memberName) : Constraint(memberName)
{
    public override Value BuildInterpretationTree(RuleInterpretationContext context) {
        Value member = context.GetMemberAccessor(PropertyName);
        return new NotEqual(member, Literal.Null);
    }
}
