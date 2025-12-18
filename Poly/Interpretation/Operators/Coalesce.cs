using Poly.Introspection;

namespace Poly.Interpretation.Operators;

/// <summary>
/// Represents a null-coalescing operation that returns the left-hand value if it's not null, otherwise returns the right-hand value.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.Coalesce"/> which evaluates the left operand and returns it if non-null,
/// otherwise evaluates and returns the right operand.
/// Corresponds to the <c>??</c> operator in C#.
/// </remarks>
public sealed class Coalesce(Value leftHandValue, Value rightHandValue) : Operator {
    /// <summary>
    /// Gets the left-hand operand (the value to test for null).
    /// </summary>
    public Value LeftHandValue { get; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));

    /// <summary>
    /// Gets the right-hand operand (the fallback value if left is null).
    /// </summary>
    public Value RightHandValue { get; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    /// <inheritdoc />
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        // The result type is typically the non-nullable version of the left type,
        // but for simplicity we return the right-hand type which should be the target type
        return RightHandValue.GetTypeDefinition(context);
    }

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        Expression leftExpr = LeftHandValue.BuildExpression(context);
        Expression rightExpr = RightHandValue.BuildExpression(context);

        return Expression.Coalesce(leftExpr, rightExpr);
    }

    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} ?? {RightHandValue})";
}