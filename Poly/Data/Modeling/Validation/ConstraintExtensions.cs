namespace Poly.Data.Modeling.Validation;

public static class ConstraintExtensions {
    extension(Constraint constraint) {
        /// <summary>
        /// Returns true if this constraint can be applied to a type with the given categories.
        /// </summary>
        public bool IsApplicableTo(TypeCategory typeCategories) {
            // If no specific categories are required, the constraint is universally applicable
            if (constraint.ApplicableCategories == TypeCategory.None)
                return true;

            // Check if any of the applicable categories are present in the type
            return (constraint.ApplicableCategories & typeCategories) != TypeCategory.None;
        }
    }
}