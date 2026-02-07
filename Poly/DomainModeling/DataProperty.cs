using Poly.DomainModeling.TypeExpressions;
using Poly.Validation;

namespace Poly.DomainModeling;

/// <summary>
/// Represents a property in a data model type.
/// </summary>
/// <param name="Name">The name of the property.</param>
/// <param name="Type">The type expression defining this property's type.</param>
/// <param name="Constraints">Validation constraints applied to this property.</param>
/// <param name="DefaultValue">Optional default value for this property.</param>
public sealed record DataProperty(
    string Name,
    TypeExpression Type,
    IEnumerable<Constraint> Constraints,
    object? DefaultValue = null
) {
    /// <summary>
    /// Returns true if this property is nullable/optional.
    /// </summary>
    public bool IsNullable => Type.IsNullable;

    /// <summary>
    /// Returns true if this property is a collection type.
    /// </summary>
    public bool IsCollection => Type.IsCollection;

    /// <summary>
    /// Returns true if this property references another type in the model.
    /// </summary>
    public bool IsReference => Type.IsReference;

    /// <summary>
    /// Gets the type categories that apply to this property.
    /// </summary>
    public TypeCategory Category => Type.Category;

    public override string ToString() => $"{Type} {Name}";
}