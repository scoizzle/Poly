using Poly.Interpretation;
using Poly.Interpretation.Operators.Equality;

namespace Poly.Validation.Constraints;

public sealed class EqualityConstraint(object value) : Constraint {
    public object Value { get; set; } = value;

    public override Value BuildInterpretationTree(RuleBuildingContext context) {
        var member = context.Value;
        var valueLiteral = new Literal(Value);
        return new Equal(member, valueLiteral);
    }

    public override string ToString() => $"value == {Value}";
}