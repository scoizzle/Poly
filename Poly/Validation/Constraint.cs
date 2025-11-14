using System.Text.Json.Serialization;

namespace Poly.Validation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "ConstraintType")]
[JsonDerivedType(typeof(RangeConstraint), "Range")]
[JsonDerivedType(typeof(NotNullConstraint), "NotNull")]
[JsonDerivedType(typeof(LengthConstraint), "Length")]
[JsonDerivedType(typeof(Constraints.EqualityConstraint), "Equality")]
public abstract class Constraint : Rule {
}