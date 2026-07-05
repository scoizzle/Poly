namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an multiplication operation between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Multiply"/> which performs numeric multiplication.
/// Corresponds to the <c>*</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Multiply(Node LeftHandValue, Node RightHandValue) : Expression {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];

    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} * {RightHandValue})";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        foreach (var p in LeftHandValue.ToPrimitives(context)) yield return p;
        foreach (var p in RightHandValue.ToPrimitives(context)) yield return p;
        yield return new Primitives.BinaryOp(Poly.Syntax.Primitives.OpKind.Mul);
    }
}