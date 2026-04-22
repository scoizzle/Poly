namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a safe cast operation that returns null when a cast cannot be performed.
/// </summary>
/// <remarks>
/// Corresponds to the <c>value as TargetType</c> operator in C#.
/// The target type is specified by <see cref="TargetTypeReference"/>; semantic analysis passes resolve it to an ITypeDefinition.
/// </remarks>
public sealed record TypeAs : Operator {
    public TypeAs(Node operand, Node targetTypeReference) {
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        TargetTypeReference = targetTypeReference ?? throw new ArgumentNullException(nameof(targetTypeReference));
    }

    public Node Operand { get; }

    public Node TargetTypeReference { get; }

    public override IEnumerable<Node?> Children => [Operand, TargetTypeReference];

    public override string ToString() => $"({Operand} as {TargetTypeReference})";
}