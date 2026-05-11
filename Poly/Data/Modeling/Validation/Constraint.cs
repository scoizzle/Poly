namespace Poly.Data.Modeling.Validation;

public interface Constraint {
    /// <summary>
    /// Gets the type categories this constraint can be applied to.
    /// Returns <see cref="TypeCategory.None"/> if the constraint is universally applicable.
    /// </summary>
    public TypeCategory ApplicableCategories { get; }
}