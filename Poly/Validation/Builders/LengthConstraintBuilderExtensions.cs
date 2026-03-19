namespace Poly.Validation.Builders;

public static class LengthConstraintBuilderExtensions {
    public static ConstraintSetBuilder<string> MinLength(this ConstraintSetBuilder<string> builder, int minLength) {
        LengthConstraint? existingLengthConstraint = builder.Constraints
            .OfType<LengthConstraint>()
            .FirstOrDefault();

        if (existingLengthConstraint != null) {
            existingLengthConstraint.MinLength = minLength;
            return builder;
        }

        return builder.Add(new LengthConstraint(minLength, null));
    }

    public static ConstraintSetBuilder<string> MaxLength(this ConstraintSetBuilder<string> builder, int maxLength) {
        LengthConstraint? existingLengthConstraint = builder.Constraints
            .OfType<LengthConstraint>()
            .FirstOrDefault();

        if (existingLengthConstraint != null) {
            existingLengthConstraint.MaxLength = maxLength;
            return builder;
        }

        return builder.Add(new LengthConstraint(null, maxLength));
    }

    public static ConstraintSetBuilder<T[]> MinLength<T>(this ConstraintSetBuilder<T[]> builder, int minLength) {
        LengthConstraint? existingLengthConstraint = builder.Constraints
            .OfType<LengthConstraint>()
            .FirstOrDefault();

        if (existingLengthConstraint != null) {
            existingLengthConstraint.MinLength = minLength;
            return builder;
        }

        return builder.Add(new LengthConstraint(minLength, null));
    }

    public static ConstraintSetBuilder<T[]> MaxLength<T>(this ConstraintSetBuilder<T[]> builder, int maxLength) {
        LengthConstraint? existingLengthConstraint = builder.Constraints
            .OfType<LengthConstraint>()
            .FirstOrDefault();

        if (existingLengthConstraint != null) {
            existingLengthConstraint.MaxLength = maxLength;
            return builder;
        }

        return builder.Add(new LengthConstraint(null, maxLength));
    }
}