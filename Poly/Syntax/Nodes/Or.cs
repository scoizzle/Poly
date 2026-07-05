namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a logical OR operation (short-circuiting) between two boolean values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.OrElse"/> which implements short-circuit evaluation:
/// if the left operand is true, the right operand is not evaluated.
/// Corresponds to the <c>||</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Or(Node LeftHandValue, Node RightHandValue) : Expression {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];

    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} || {RightHandValue}";
    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        foreach (var p in LeftHandValue.ToPrimitives(context)) yield return p;
        foreach (var p in RightHandValue.ToPrimitives(context)) yield return p;
        yield return new Primitives.BinaryOp(Poly.Syntax.Primitives.OpKind.Or);
    }
}