using System.Text.Json.Serialization;
using Poly.Interpretation.AbstractSyntaxTree;

namespace Poly.Validation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(RangeConstraint), "Range")]
[JsonDerivedType(typeof(NotNullConstraint), "NotNull")]
[JsonDerivedType(typeof(LengthConstraint), "Length")]
[JsonDerivedType(typeof(Constraints.EqualityConstraint), "Equality")]
public abstract class Constraint : Rule {
}