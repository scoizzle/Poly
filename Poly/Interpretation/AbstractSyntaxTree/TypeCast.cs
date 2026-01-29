namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents an explicit type cast operation that converts a value to a specified type.
/// </summary>
/// <remarks>
/// Corresponds to the <c>(TargetType)value</c> cast operator in C#.
/// The <see cref="IsChecked"/> flag indicates whether overflow checking is enabled.
/// The target type is specified by <see cref="TargetTypeReference"/>; semantic analysis passes resolve it to an ITypeDefinition.
/// </remarks>
public sealed record TypeCast : Operator {
    public TypeCast(Node operand, Node targetTypeReference, bool isChecked = false)
    {
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        TargetTypeReference = targetTypeReference ?? throw new ArgumentNullException(nameof(targetTypeReference));
        IsChecked = isChecked;
    }

    public Node Operand { get; }

    public Node TargetTypeReference { get; }

    public bool IsChecked { get; }

    /// <inheritdoc />
    public override string ToString() => $"(({TargetTypeReference}){Operand})";
}