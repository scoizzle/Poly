namespace Poly.Interpretation.AbstractSyntaxTree.Arithmetic;

/// <summary>
/// Represents an modulo operation between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Modulo"/> which performs numeric modulo.
/// Corresponds to the <c>%</c> operator in C#.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Modulo : Operator
{
    public Modulo(Node leftHandValue, Node rightHandValue)
    {
        LeftHandValue = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));
        RightHandValue = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));
    }

    public Node LeftHandValue { get; }

    public Node RightHandValue { get; }

    /// <inheritdoc />
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];

    /// <inheritdoc />
    public override string ToString() => $"({LeftHandValue} % {RightHandValue})";
}