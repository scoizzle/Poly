using Poly.Interpretation.AbstractSyntaxTree.Equality;
using Poly.Introspection;

using static Poly.Interpretation.AbstractSyntaxTree.NodeExtensions;

namespace Poly.Validation.Constraints;

public sealed class EqualityConstraint(object value) : Constraint {
    public object Value { get; set; } = value;

    /// <summary>
    /// Equality constraint is universally applicable to any type that supports equality.
    /// </summary>
    public override TypeCategory ApplicableCategories => TypeCategory.None;

    public override Node BuildInterpretationTree(RuleBuildingContext context)
    {
        var member = context.Value;
        var valueLiteral = Wrap(Value);
        var equalityCheck = new Equal(member, valueLiteral);
        return equalityCheck;
    }

    public override string ToString() => $"value == {Value}";
}