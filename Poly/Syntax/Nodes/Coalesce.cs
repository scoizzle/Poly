namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a null-coalescing operation that returns the left-hand value if it's not null, otherwise returns the right-hand value.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Coalesce"/> which evaluates the left operand and returns it if non-null,
/// otherwise evaluates and returns the right operand.
/// Corresponds to the <c>??</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Coalesce : Expression {
    public Coalesce(Node leftHandValue, Node rightHandValue) {
        LeftHandValue = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));
        RightHandValue = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));
    }

    public Node LeftHandValue { get; }

    public Node RightHandValue { get; }

    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];

    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} ?? {RightHandValue})";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        var nullLabel = new Poly.Syntax.Primitives.Label("coalesce_null");

        // Emit lhs, duplicate — one copy for the null check, one to keep
        foreach (var p in LeftHandValue.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.Dup();
        yield return new Poly.Syntax.Primitives.CondGoto(nullLabel);

        // lhs is not null — stack has the second dup copy; return it
        yield return new Poly.Syntax.Primitives.Return();

        // lhs was null — discard the dup copy, emit rhs, return it
        yield return nullLabel;
        yield return new Poly.Syntax.Primitives.Discard();
        foreach (var p in RightHandValue.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.Return();
    }
}