using Poly.Interpretation;
using Poly.Interpretation.Operators.Equality;

namespace Poly.Validation;

public sealed class NotNullConstraint(string memberName) : Constraint(memberName) {
    public override Value BuildInterpretationTree(RuleInterpretationContext context) {
        Value member = context.GetMemberAccessor(PropertyName);
        return new NotEqual(member, Value.Null);
    }

    public override string ToString() => $"{PropertyName} != null";
}