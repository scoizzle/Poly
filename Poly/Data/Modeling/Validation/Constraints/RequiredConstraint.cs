namespace Poly.Data.Modeling.Validation.Constraints;

public sealed class RequiredConstraint : Constraint {
    /// <summary>
    /// Required constraint is universally applicable - it makes sense for any nullable type
    /// and is a no-op for non-nullable types.
    /// </summary>
    public TypeCategory ApplicableCategories => TypeCategory.None;
}