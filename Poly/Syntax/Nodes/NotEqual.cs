namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an inequality comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.NotEqual"/> which tests if two values are not equal.
/// Corresponds to the <c>!=</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record NotEqual(Node LeftHandValue, Node RightHandValue) : Expression {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];

    public override string ToString() => $"{LeftHandValue} != {RightHandValue}";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        foreach (var p in LeftHandValue.ToPrimitives(context)) yield return p;
        foreach (var p in RightHandValue.ToPrimitives(context)) yield return p;
        yield return new Poly.Syntax.Primitives.BinaryOp(Poly.Syntax.Primitives.OpKind.Neq);
    }
}