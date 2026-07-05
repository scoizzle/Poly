namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a less-than comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.LessThan"/> which tests if the left value is less than the right value.
/// Corresponds to the <c>&lt;</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record LessThan(Node LeftHandValue, Node RightHandValue) : Expression {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];

    public override string ToString() => $"{LeftHandValue} < {RightHandValue}";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        foreach (var p in LeftHandValue.ToPrimitives(context)) yield return p;
        foreach (var p in RightHandValue.ToPrimitives(context)) yield return p;
        yield return new Primitives.BinaryOp(Poly.Syntax.Primitives.OpKind.Lt);
    }
}