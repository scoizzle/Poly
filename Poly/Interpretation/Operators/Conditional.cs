using Poly.Introspection;

namespace Poly.Interpretation.Operators;

/// <summary>
/// Represents a conditional (ternary) expression that evaluates one of two values based on a condition.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.Condition"/> which evaluates the condition and returns either
/// the true value or the false value accordingly.
/// Corresponds to the <c>condition ? trueValue : falseValue</c> operator in C#.
/// </remarks>
public sealed class Conditional(Value condition, Value ifTrue, Value ifFalse) : Operator {
    /// <summary>
    /// Gets the condition to evaluate.
    /// </summary>
    public Value Condition { get; } = condition ?? throw new ArgumentNullException(nameof(condition));
    
    /// <summary>
    /// Gets the value to return if the condition is true.
    /// </summary>
    public Value IfTrue { get; } = ifTrue ?? throw new ArgumentNullException(nameof(ifTrue));
    
    /// <summary>
    /// Gets the value to return if the condition is false.
    /// </summary>
    public Value IfFalse { get; } = ifFalse ?? throw new ArgumentNullException(nameof(ifFalse));

    /// <inheritdoc />
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        // The result type is the type of the true branch
        // (both branches should have compatible types, but we'll let the expression tree handle validation)
        return IfTrue.GetTypeDefinition(context);
    }

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        Expression conditionExpr = Condition.BuildExpression(context);
        Expression ifTrueExpr = IfTrue.BuildExpression(context);
        Expression ifFalseExpr = IfFalse.BuildExpression(context);
        
        return Expression.Condition(conditionExpr, ifTrueExpr, ifFalseExpr);
    }

    /// <inheritdoc />
    public override string ToString() => $"({Condition} ? {IfTrue} : {IfFalse})";
}
