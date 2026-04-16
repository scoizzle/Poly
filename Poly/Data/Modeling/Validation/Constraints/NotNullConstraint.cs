using Poly.Interpretation.AbstractSyntaxTree.Equality;

using static Poly.Interpretation.AbstractSyntaxTree.NodeExtensions;

namespace Poly.Data.Modeling.Validation.Constraints;

public sealed class NotNullConstraint : Constraint {
    /// <summary>
    /// NotNull constraint is universally applicable - it makes sense for any nullable type
    /// and is a no-op for non-nullable types.
    /// </summary>
    public TypeCategory ApplicableCategories => TypeCategory.None;

    public Node ToInterpretationNode(Node value) {
        var notNullCheck = new NotEqual(value, Null);
        return notNullCheck;
    }
}