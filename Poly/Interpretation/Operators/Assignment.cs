using Poly.Introspection;

namespace Poly.Interpretation.Operators;

/// <summary>
/// Represents an assignment operation that assigns a value to a destination.
/// </summary>
/// <remarks>
/// Compiles to a <see cref="System.Linq.Expressions.BinaryExpression"/> with 
/// <see cref="System.Linq.Expressions.ExpressionType.Assign"/> node type.
/// The destination must be an assignable expression (variable, parameter, member, etc.).
/// </remarks>
public sealed class Assignment(Value destination, Value value) : Operator {
    /// <summary>
    /// Gets the destination of the assignment (left-hand side).
    /// </summary>
    public Value Destination { get; init; } = destination ?? throw new ArgumentNullException(nameof(destination));
    
    /// <summary>
    /// Gets the value to assign (right-hand side).
    /// </summary>
    public Value Value { get; init; } = value ?? throw new ArgumentNullException(nameof(value));

    /// <inheritdoc />
    /// <returns>The type of the destination.</returns>
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) => Destination.GetTypeDefinition(context);

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        Expression destExpr = Destination.BuildExpression(context);
        Expression valueExpr = Value.BuildExpression(context);
        return Expression.Assign(destExpr, valueExpr);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Destination} = {Value}";
}