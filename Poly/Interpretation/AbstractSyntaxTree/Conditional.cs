namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a conditional (ternary) expression that evaluates one of two values based on a condition.
/// </summary>
/// <remarks>
/// Evaluates the condition and returns either the true value or the false value accordingly.
/// Corresponds to the <c>condition ? trueValue : falseValue</c> operator in C#.
/// Type information is resolved by semantic analysis passes (INodeAnalyzer implementations).
/// </remarks>
public sealed record Conditional(Node Condition, Node IfTrue, Node IfFalse) : Operator {
    public override IEnumerable<Node?> Children => [Condition, IfTrue, IfFalse];
    /// <inheritdoc />
    public override string ToString() => $"({Condition} ? {IfTrue} : {IfFalse})";
}