using Poly.Interpretation;
using Poly.Interpretation.Expressions;

namespace Poly.Validation.Constraints;

public sealed class EqualityConstraint(object value) : Constraint {
    public object Value { get; set; } = value;

    public override Interpretable BuildInterpretationTree(RuleBuildingContext context)
    {
        var member = context.Value;
        var valueLiteral = new Constant(Value);
        var equalityCheck = new BinaryOperation(BinaryOperationKind.Equal, member, valueLiteral);
        return equalityCheck;
    }

    public override string ToString() => $"value == {Value}";
}