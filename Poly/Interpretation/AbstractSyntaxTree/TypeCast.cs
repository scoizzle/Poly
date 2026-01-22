namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents an explicit type cast operation that converts a value to a specified type.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Convert"/> which performs an explicit type conversion.
/// Corresponds to the <c>(TargetType)value</c> cast operator in C#.
/// For checked conversions that throw on overflow, use <see cref="Expr.ConvertChecked"/>.
/// The target type is specified by name; semantic analysis middleware resolves it to an ITypeDefinition.
/// </remarks>
public sealed record TypeCast : Operator
{
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
