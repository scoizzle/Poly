namespace Poly.Data.Modeling.Validation.Constraints;

/// <summary>
/// Represents a constraint that enforces a value to be within a specified range.
/// </summary>
/// <param name="minValue">The minimum allowable value.</param>
/// <param name="maxValue">The maximum allowable value.</param>
public sealed class RangeConstraint(object? minValue, object? maxValue) : Constraint {
    /// <summary>
    /// The minimum allowable value.
    /// </summary>
    public object? MinValue { get; } = minValue;

    /// <summary>
    /// The maximum allowable value.
    /// </summary>
    public object? MaxValue { get; } = maxValue;

    /// <inheritdoc />
    public TypeCategory ApplicableCategories => TypeCategory.Numeric | TypeCategory.Temporal;
}