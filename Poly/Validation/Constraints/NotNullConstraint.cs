using Poly.Interpretation.AbstractSyntaxTree.Equality;

using static Poly.Interpretation.AbstractSyntaxTree.NodeExtensions;

namespace Poly.Validation;

public sealed class NotNullConstraint : Constraint {
    public override Node BuildInterpretationTree(RuleBuildingContext context)
    {
        var notNullCheck = new NotEqual(context.Value, Null);
        return notNullCheck;
    }

    public override string ToString() => "value != null";
}