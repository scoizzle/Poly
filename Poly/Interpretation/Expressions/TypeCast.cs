namespace Poly.Interpretation.Expressions;

using Poly.Introspection;

/// <summary>
/// Represents a type cast operation in an interpretation tree.
/// </summary>
public sealed class TypeCast(Interpretable operand, ITypeDefinition targetType) : Interpretable {
    /// <summary>
    /// Gets the value to cast.
    /// </summary>
    public Interpretable Operand { get; } = operand ?? throw new ArgumentNullException(nameof(operand));

    /// <summary>
    /// Gets the target type to cast to.
    /// </summary>
    public ITypeDefinition TargetType { get; } = targetType ?? throw new ArgumentNullException(nameof(targetType));

    /// <inheritdoc />
    public override TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder)
    {
        var value = Operand.Evaluate(builder);
        return builder.TypeCast(value, TargetType);
    }

    /// <inheritdoc />
    public override string ToString() => $"(({TargetType.Name}){Operand})";
}