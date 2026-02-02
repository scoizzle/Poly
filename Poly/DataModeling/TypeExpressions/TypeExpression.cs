using Poly.Introspection;

namespace Poly.DataModeling.TypeExpressions;

/// <summary>
/// A composable type expression for modeling any domain type.
/// Types are built compositionally from primitives and type constructors.
/// </summary>
public abstract record TypeExpression {
    /// <summary>
    /// Gets the categories that apply to this type expression.
    /// </summary>
    public abstract TypeCategory Category { get; }

    /// <summary>
    /// Returns true if this type has the specified category flag.
    /// </summary>
    public bool HasCategory(TypeCategory category) => (Category & category) == category;

    /// <summary>
    /// Returns true if this type is nullable/optional.
    /// </summary>
    public bool IsNullable => HasCategory(TypeCategory.Nullable);

    /// <summary>
    /// Returns true if this type is a collection (array, list, set).
    /// </summary>
    public bool IsCollection => HasCategory(TypeCategory.Collection);

    /// <summary>
    /// Returns true if this type is numeric (integer or floating point).
    /// </summary>
    public bool IsNumeric => HasCategory(TypeCategory.Numeric);

    /// <summary>
    /// Returns true if this type is a reference to another type in the model.
    /// </summary>
    public bool IsReference => HasCategory(TypeCategory.Reference);
}