namespace Poly.Interpretation.Expressions;

/// <summary>
/// Represents a constant value in an interpretation tree.
/// </summary>
/// <remarks>
/// Constants are immutable values that are known at interpretation time and compile
/// to <see cref="System.Linq.Expressions.ConstantExpression"/> nodes.
/// </remarks>
public sealed class Constant : Interpretable {
    public object? Value { get; }

    public Constant(object? value)
    {
        Value = value;
    }

    /// <inheritdoc />


    public override TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder)
        => builder.Constant(Value!);

    public override string ToString() => Value?.ToString() ?? "null";
}