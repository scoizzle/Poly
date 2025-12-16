using Poly.Introspection;

namespace Poly.Interpretation.Operators.Arithmetic;

/// <summary>
/// Represents a unary negation operation that negates a numeric value.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.Negate"/> which returns the arithmetic negation of the operand.
/// Corresponds to the <c>-</c> prefix operator in C#.
/// </remarks>
public sealed class UnaryMinus(Value operand) : Operator {
    /// <summary>
    /// Gets the operand to negate.
    /// </summary>
    public Value Operand { get; init; } = operand ?? throw new ArgumentNullException(nameof(operand));

    /// <inheritdoc />
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        return Operand.GetTypeDefinition(context);
    }

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        Expression operandExpr = Operand.BuildExpression(context);
        return Expression.Negate(operandExpr);
    }

    /// <inheritdoc />
    public override string ToString() => $"-{Operand}";
}
