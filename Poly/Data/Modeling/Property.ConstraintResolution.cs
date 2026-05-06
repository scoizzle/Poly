using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling;

public sealed partial record Property {
    /// <summary>
    /// Gets effective constraints for this property after merging type-level and property-level constraints.
    /// Property-level EnumConstraint overrides type-level EnumConstraint.
    /// </summary>
    public IReadOnlyCollection<Constraint> EffectiveConstraints {
        get {
            var hasPropertyEnumConstraint = Constraints.Any(static constraint => constraint.IsOrContains<EnumConstraint>());

            if (!hasPropertyEnumConstraint) {
                return [.. Type.Constraints, .. Constraints];
            }

            var typeConstraintsWithoutEnum = Type.Constraints
                .Where(static constraint => !constraint.IsOrContains<EnumConstraint>());

            return [.. typeConstraintsWithoutEnum, .. Constraints];
        }
    }
}