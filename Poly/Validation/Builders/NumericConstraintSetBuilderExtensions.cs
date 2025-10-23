namespace Poly.Validation.Builders;

public static class NumericConstraintSetBuilderExtensions {
    public static ConstraintSetBuilder<T> Minimum<T, TProp>(this ConstraintSetBuilder<T> builder, TProp value)
        where TProp : INumber<TProp> {
        RangeConstraint? existingRangeConstraint = builder.Constraints
            .OfType<RangeConstraint>()
            .FirstOrDefault();

        if (existingRangeConstraint != null) {
            existingRangeConstraint.MinValue = value;
            return builder;
        }

        return builder.Add(new RangeConstraint(builder.PropertyName, value, null));
    }

    public static ConstraintSetBuilder<T> Maximum<T, TProp>(this ConstraintSetBuilder<T> builder, TProp value)
        where TProp : INumber<TProp> {
        RangeConstraint? existingRangeConstraint = builder.Constraints
            .OfType<RangeConstraint>()
            .FirstOrDefault();

        if (existingRangeConstraint != null) {
            existingRangeConstraint.MaxValue = value;
            return builder;
        }

        return builder.Add(new RangeConstraint(builder.PropertyName, null, value));
    }
}
