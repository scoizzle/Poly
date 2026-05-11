namespace Poly.Data.Modeling.Validation.Constraints;

public sealed class LengthConstraint(int? minLength = default, int? maxLength = default) : Constraint {
    public int? MinLength { get; set; } = minLength;
    public int? MaxLength { get; set; } = maxLength;

    /// <summary>
    /// Length constraints apply to Text and Binary types.
    /// </summary>
    public TypeCategory ApplicableCategories => TypeCategory.Text | TypeCategory.Binary;
}