using Poly.Syntax.AbstractSyntaxTree;

using static Poly.Syntax.AbstractSyntaxTree.NodeExtensions;

namespace Poly.Data.Modeling.Validation.Constraints;

public sealed class EqualityConstraint(object value) : Constraint {
    public object Value { get; set; } = value;

    /// <summary>
    /// Equality constraint is universally applicable to any type that supports equality.
    /// </summary>
    public TypeCategory ApplicableCategories => TypeCategory.None;

    public Node ToInterpretationNode(Node value) {
        var valueLiteral = Wrap(Value);
        var equalityCheck = new Equal(value, valueLiteral);
        return equalityCheck;
    }
}