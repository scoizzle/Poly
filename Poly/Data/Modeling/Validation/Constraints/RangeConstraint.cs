using Poly.Syntax.AbstractSyntaxTree;

using static Poly.Syntax.AbstractSyntaxTree.NodeExtensions;

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
    public object? MinValue { get; set; } = minValue;

    /// <summary>
    /// The maximum allowable value.
    /// </summary>
    public object? MaxValue { get; set; } = maxValue;

    /// <inheritdoc />
    public TypeCategory ApplicableCategories => TypeCategory.Numeric | TypeCategory.Temporal;

    /// <inheritdoc />
    public Node ToInterpretationNode(Node value) {
        Node? minCheck = MinValue is null
            ? null
            : new GreaterThanOrEqual(value, Wrap(MinValue));

        Node? maxCheck = MaxValue is null
            ? null
            : new LessThanOrEqual(value, Wrap(MaxValue));

        var rangeCheck = (minCheck, maxCheck) switch {
            (Node min, Node max) => new And(min, max),
            (Node min, null) => min,
            (null, Node max) => max,
            _ => True
        };

        return rangeCheck;
    }
}