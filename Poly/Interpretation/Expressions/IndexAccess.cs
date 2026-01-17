namespace Poly.Interpretation.Expressions;

/// <summary>
/// Represents an index access operation (indexer access) in an interpretation tree.
/// </summary>
/// <remarks>
/// This operator enables accessing indexed members of a value using bracket notation (e.g., <c>array[0]</c> or <c>dictionary["key"]</c>).
/// The indexer is resolved at interpretation time using the type definition system, selecting the best match based on index argument types.
/// </remarks>
public sealed class IndexAccess(Interpretable target, params IEnumerable<Interpretable> arguments) : Interpretable {
    /// <summary>
    /// Gets the value whose indexer is being accessed.
    /// </summary>
    public Interpretable Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    /// <summary>
    /// Gets the index arguments for the indexer.
    /// </summary>
    public IEnumerable<Interpretable> Arguments { get; } = arguments ?? throw new ArgumentNullException(nameof(arguments));

    /// <inheritdoc />


    public override TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder)
    {
        var instance = Target.Evaluate(builder);
        var indices = Arguments.Select(arg => arg.Evaluate(builder));
        return builder.IndexGet(instance, indices);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Target}[{string.Join(", ", Arguments)}]";
}