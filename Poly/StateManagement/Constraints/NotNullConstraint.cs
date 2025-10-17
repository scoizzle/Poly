using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;

namespace Poly.StateManagement;

public sealed record NotNullConstraint(string resourceProperty) : Constraint(resourceProperty)
{
    public override Value BuildInterpretationTree(RuleInterpretationContext context) {
        Value member = context.GetMemberAccess(Member);
        return new InequalityOperator(member, Literal.Null);
    }
}
