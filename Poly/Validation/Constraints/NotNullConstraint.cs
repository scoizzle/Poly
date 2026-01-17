using Poly.Interpretation;
using Poly.Interpretation.Expressions;

namespace Poly.Validation;

public sealed class NotNullConstraint : Constraint {
    public override Interpretable BuildInterpretationTree(RuleBuildingContext context)
    {
        var notNullCheck = new BinaryOperation(BinaryOperationKind.NotEqual, context.Value, new Constant(null));
        return notNullCheck;
    }

    public override string ToString() => "value != null";
}