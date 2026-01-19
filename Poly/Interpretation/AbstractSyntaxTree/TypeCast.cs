namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents an explicit type cast operation that converts a value to a specified type.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Convert"/> which performs an explicit type conversion.
/// Corresponds to the <c>(TargetType)value</c> cast operator in C#.
/// For checked conversions that throw on overflow, use <see cref="Expr.ConvertChecked"/>.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record TypeCast(Node Operand, ITypeDefinition TargetType, bool IsChecked = false) : Operator
{
    /// <inheritdoc />
    public override TResult Transform<TResult>(ITransformer<TResult> transformer) => transformer.Transform(this);

    /// <inheritdoc />
    public override string ToString() => $"(({TargetType.Name}){Operand})";
}
