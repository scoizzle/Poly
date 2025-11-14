using Poly.Interpretation;
using Poly.Interpretation.Operators.Equality;

namespace Poly.Validation;

public sealed class NotNullConstraint : Constraint {
    public override Value BuildInterpretationTree(RuleBuildingContext context) {
        return new NotEqual(context.Value, Value.Null);
    }

    public override string ToString() => "value != null";
}