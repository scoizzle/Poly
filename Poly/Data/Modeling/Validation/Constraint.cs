using Poly.Syntax;

namespace Poly.Data.Modeling.Validation;

public interface Constraint {
    /// <summary>
    /// Gets the type categories this constraint can be applied to.
    /// Returns <see cref="TypeCategory.None"/> if the constraint is universally applicable.
    /// </summary>
    public TypeCategory ApplicableCategories { get; }

    /// <summary>
    /// Builds an interpretation tree representing the logic of this constraint for the given context.
    /// </summary>
    /// <param name="value">The value node to which the constraint is applied.</param>
    /// <returns>A <see cref="Node"/> representing the interpretation tree for this constraint.</returns>
    public Node ToInterpretationNode(Node value);
}