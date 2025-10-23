namespace Poly.Validation.Builders;

public static class NotNullConstraintBuilderExtensions {
    public static ConstraintSetBuilder<T?> NotNull<T>(this ConstraintSetBuilder<T?> builder) where T : class {
        return builder.Add(new NotNullConstraint(builder.PropertyName));
    }
}