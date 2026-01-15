namespace Poly.Interpretation.Operators.Boolean;

/// <summary>
/// Represents a logical NOT operation (negation) of a boolean value.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.Not"/> which inverts the boolean value.
/// Corresponds to the <c>!</c> operator in C#.
/// </remarks>
public sealed class Not(Value value) : BooleanOperator {
    /// <summary>
    /// Gets the value to negate.
    /// </summary>
    public Value Value { get; init; } = value ?? throw new ArgumentNullException(nameof(value));

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context)
    {
        var innerExpression = Value.BuildExpression(context);
        return Expression.Not(innerExpression);
    }

    /// <inheritdoc />
    public override string ToString() => $"!{Value}";
}