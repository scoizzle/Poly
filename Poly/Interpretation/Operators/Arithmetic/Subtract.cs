using Poly.Introspection;

namespace Poly.Interpretation.Operators.Arithmetic;

/// <summary>
/// Represents a subtraction operation between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.Subtract"/> which performs numeric subtraction.
/// Corresponds to the <c>-</c> operator in C#.
/// </remarks>
public sealed class Subtract(Value leftHandValue, Value rightHandValue) : Operator {
    /// <summary>
    /// Gets the left-hand operand of the subtraction.
    /// </summary>
    public Value LeftHandValue { get; init; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));
    
    /// <summary>
    /// Gets the right-hand operand of the subtraction.
    /// </summary>
    public Value RightHandValue { get; init; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    /// <inheritdoc />
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        // Return the type of the left operand (assumes both operands have compatible types)
        return LeftHandValue.GetTypeDefinition(context);
    }

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        Expression leftExpr = LeftHandValue.BuildExpression(context);
        Expression rightExpr = RightHandValue.BuildExpression(context);
        return Expression.Subtract(leftExpr, rightExpr);
    }

    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} - {RightHandValue})";
}