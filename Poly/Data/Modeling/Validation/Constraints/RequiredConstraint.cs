namespace Poly.Data.Modeling.Validation.Constraints;

public sealed class RequiredConstraint : Constraint {
    /// <summary>
    /// Required is universally applicable and acts as the domain-level nullability signal.
    /// </summary>
    public TypeCategory ApplicableCategories => TypeCategory.None;
}