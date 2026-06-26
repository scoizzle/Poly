namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a greater-than comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.GreaterThan"/> which tests if the left value is greater than the right value.
/// Corresponds to the <c>&gt;</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record GreaterThan(Node LeftHandValue, Node RightHandValue) : Expression {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];

    public override string ToString() => $"{LeftHandValue} > {RightHandValue}";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        foreach (var p in LeftHandValue.ToPrimitives(context)) yield return p;
        foreach (var p in RightHandValue.ToPrimitives(context)) yield return p;
        yield return new Poly.Syntax.Primitives.BinaryOp(Poly.Syntax.Primitives.OpKind.Gt);
    }
}