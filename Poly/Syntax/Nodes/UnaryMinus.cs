namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a unary negation operation that negates a numeric value.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Negate"/> which returns the arithmetic negation of the operand.
/// Corresponds to the <c>-</c> prefix operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record UnaryMinus(Node Operand) : Expression {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [Operand];

    /// <inheritdoc />
    public override string ToString() => $"-{Operand}";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        foreach (var p in Operand.ToPrimitives(context)) yield return p;
        yield return new Poly.Syntax.Primitives.UnaryOp(Poly.Syntax.Primitives.UnaryOpKind.Neg);
    }
}