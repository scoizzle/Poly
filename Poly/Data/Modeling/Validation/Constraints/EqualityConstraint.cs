namespace Poly.Data.Modeling.Validation.Constraints;

public sealed class EqualityConstraint(object value) : Constraint {
    public object Value { get; set; } = value;

    /// <summary>
    /// Equality constraint is universally applicable to any type that supports equality.
    /// </summary>
    public TypeCategory ApplicableCategories => TypeCategory.None;
}