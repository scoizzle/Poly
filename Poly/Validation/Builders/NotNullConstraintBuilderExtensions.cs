namespace Poly.Validation.Builders;
using Poly.Interpretation.AbstractSyntaxTree;

public static class NotNullConstraintBuilderExtensions {
    // For nullable reference types
    public static ConstraintSetBuilder<T?> NotNull<T>(this ConstraintSetBuilder<T?> builder) where T : class
    {
        return builder.Add(new NotNullConstraint());
    }

    // For nullable value types
    public static ConstraintSetBuilder<T?> NotNull<T>(this ConstraintSetBuilder<T?> builder) where T : struct
    {
        return builder.Add(new NotNullConstraint());
    }
}