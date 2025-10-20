using Poly.Interpretation;
using Poly.Interpretation.Operators.Boolean;

namespace Poly.StateManagement.Constraints;

public sealed record MaxValueConstraint(string memberName, object maxValue) : Constraint(memberName) {
    public object MaxValue { get; set; } = maxValue;

    public override Value BuildInterpretationTree(RuleInterpretationContext context) {
        Literal maxValueLiteral = new(MaxValue);
        Value memberValue = context.GetMemberAccessor(PropertyName);
        return new LessThanOrEqual(memberValue, maxValueLiteral);

    }

    public override string ToString() => $"{PropertyName} <= {MaxValue}";
}