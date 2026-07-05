namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an division operation between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Divide"/> which performs numeric division.
/// Corresponds to the <c>/</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Divide(Node LeftHandValue, Node RightHandValue) : Expression {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];

    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} / {RightHandValue})";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        foreach (var p in LeftHandValue.ToPrimitives(context)) yield return p;
        foreach (var p in RightHandValue.ToPrimitives(context)) yield return p;
        yield return new Primitives.BinaryOp(Poly.Syntax.Primitives.OpKind.Div);
    }
}