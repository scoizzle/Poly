namespace Poly.Interpretation;

/// <summary>
/// Represents a named reference to a variable in an interpretation tree.
/// </summary>
/// <remarks>
/// Named references are used to load variables by name from the builder's scope, and do not hold their own value but act as pointers to variables managed by the builder.
/// </remarks>
public sealed class NamedReference(string name) : Interpretable {
    /// <summary>
    /// Gets the name of the referenced entity.
    /// </summary>
    public string Name { get; } = name;

    /// <inheritdoc />
    public override TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder)
        => builder.VariableRef(Name);
}