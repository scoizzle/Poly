namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a type test operation that checks whether a value is compatible with a target type.
/// </summary>
/// <remarks>
/// Corresponds to the <c>value is TargetType</c> operator in C#.
/// The target type is specified by <see cref="TargetTypeReference"/>; semantic analysis passes resolve it to an ITypeDefinition.
/// </remarks>
public sealed record TypeIs : Expression {
    public TypeIs(Node operand, Node targetTypeReference) {
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        TargetTypeReference = targetTypeReference ?? throw new ArgumentNullException(nameof(targetTypeReference));
    }

    public Node Operand { get; }

    public Node TargetTypeReference { get; }

    public override IEnumerable<Node?> Children => [Operand, TargetTypeReference];

    public override string ToString() => $"({Operand} is {TargetTypeReference})";
}