using System.Text.Json.Serialization;

using Poly.DomainModeling.TypeExpressions;
using Poly.Introspection;

namespace Poly.Validation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(RangeConstraint), "Range")]
[JsonDerivedType(typeof(NotNullConstraint), "NotNull")]
[JsonDerivedType(typeof(LengthConstraint), "Length")]
[JsonDerivedType(typeof(Constraints.EqualityConstraint), "Equality")]
[JsonDerivedType(typeof(DomainModeling.Builders.ValueSourceComparisonConstraint), "ValueSourceComparison")]
public abstract class Constraint : Rule {
    /// <summary>
    /// Gets the type categories this constraint can be applied to.
    /// Returns <see cref="TypeCategory.None"/> if the constraint is universally applicable.
    /// </summary>
    public abstract TypeCategory ApplicableCategories { get; }

    /// <summary>
    /// Returns true if this constraint can be applied to a type with the given categories.
    /// </summary>
    public bool IsApplicableTo(TypeCategory typeCategories)
    {
        // If no specific categories are required, the constraint is universally applicable
        if (ApplicableCategories == TypeCategory.None)
            return true;

        // Check if any of the applicable categories are present in the type
        return (ApplicableCategories & typeCategories) != TypeCategory.None;
    }

    /// <summary>
    /// Returns true if this constraint can be applied to the given type expression.
    /// </summary>
    public bool IsApplicableTo(TypeExpression typeExpression)
    {
        ArgumentNullException.ThrowIfNull(typeExpression);
        return IsApplicableTo(typeExpression.Category);
    }
}