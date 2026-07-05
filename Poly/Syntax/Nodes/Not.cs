namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a logical NOT operation (negation) of a boolean value.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Not"/> which inverts the boolean value.
/// Corresponds to the <c>!</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Not(Node Value) : Expression {
    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [Value];
    /// <inheritdoc />
    public override string ToString() => $"!{Value}";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        foreach (var p in Value.ToPrimitives(context)) yield return p;
        yield return new Primitives.UnaryOp(Poly.Syntax.Primitives.UnaryOpKind.Not);
    }
}