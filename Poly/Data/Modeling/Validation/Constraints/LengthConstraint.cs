namespace Poly.Data.Modeling.Validation.Constraints;

public sealed class LengthConstraint(int? minLength = default, int? maxLength = default) : Constraint {
    public int? MinLength { get; } = minLength;
    public int? MaxLength { get; } = maxLength;

    /// <summary>
    /// Length constraints apply to Text and Binary types.
    /// </summary>
    public TypeCategory ApplicableCategories => TypeCategory.Text | TypeCategory.Binary;
}