using Poly.Interpretation.AbstractSyntaxTree.Equality;
using Poly.Introspection;

using static Poly.Interpretation.AbstractSyntaxTree.NodeExtensions;

namespace Poly.Validation;

public sealed class NotNullConstraint : Constraint {
    /// <summary>
    /// NotNull constraint is universally applicable - it makes sense for any nullable type
    /// and is a no-op for non-nullable types.
    /// </summary>
    public override TypeCategory ApplicableCategories => TypeCategory.None;

    /// <inheritdoc />
    public override ConstraintScope Scope => ConstraintScope.Structural;

    public override Node BuildInterpretationTree(RuleBuildingContext context)
    {
        var notNullCheck = new NotEqual(context.Value, Null);
        return notNullCheck;
    }

    public override string ToString() => "value != null";
}