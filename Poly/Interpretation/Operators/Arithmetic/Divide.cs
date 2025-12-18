using Poly.Introspection;

namespace Poly.Interpretation.Operators.Arithmetic;

/// <summary>
/// Represents an division operation between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.Divide"/> which performs numeric division.
/// Corresponds to the <c>/</c> operator in C#.
/// </remarks>
public sealed class Divide(Value leftHandValue, Value rightHandValue) : Operator {
    /// <summary>
    /// Gets the left-hand operand of the division.
    /// </summary>
    public Value LeftHandValue { get; init; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));

    /// <summary>
    /// Gets the right-hand operand of the division.
    /// </summary>
    public Value RightHandValue { get; init; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    /// <inheritdoc />
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        // Determine the promoted type based on C# numeric promotion rules
        var leftType = LeftHandValue.GetTypeDefinition(context);
        var rightType = RightHandValue.GetTypeDefinition(context);
        return NumericTypePromotion.GetPromotedType(context, leftType, rightType);
    }

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        Expression leftExpr = LeftHandValue.BuildExpression(context);
        Expression rightExpr = RightHandValue.BuildExpression(context);

        var leftType = LeftHandValue.GetTypeDefinition(context);
        var rightType = RightHandValue.GetTypeDefinition(context);

        // Convert operands to promoted type
        var (convertedLeft, convertedRight) = NumericTypePromotion.ConvertToPromotedType(
            context, leftExpr, rightExpr, leftType, rightType);

        return Expression.Divide(convertedLeft, convertedRight);
    }

    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} / {RightHandValue})";
}