namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an modulo operation between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Modulo"/> which performs numeric modulo.
/// Corresponds to the <c>%</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Modulo : Expression {
    public Modulo(Node leftHandValue, Node rightHandValue) {
        LeftHandValue = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));
        RightHandValue = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));
    }

    public Node LeftHandValue { get; }

    public Node RightHandValue { get; }

    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];

    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} % {RightHandValue})";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        foreach (var p in LeftHandValue.ToPrimitives(context)) yield return p;
        foreach (var p in RightHandValue.ToPrimitives(context)) yield return p;
        yield return new Poly.Syntax.Primitives.BinaryOp(Poly.Syntax.Primitives.OpKind.Mod);
    }
}