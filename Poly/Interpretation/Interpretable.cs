namespace Poly.Interpretation;

/// <summary>
/// Base class for objects that can be interpreted and evaluated via multiple backends (LINQ, tree-walk, transpilation, etc).
/// </summary>
/// <remarks>
/// Interpretable types form an Abstract Syntax Tree (AST) that is backend-agnostic. The tree structure itself
/// contains no execution logic; instead, builders implement platform-specific logic. Nodes lower
/// to a small set of primitive operations defined by IExecutionPlanBuilder.
/// </remarks>
public abstract class Interpretable {

    /// <summary>
    /// Lowers this AST node to the execution plan builder with expression/statement separation.
    /// </summary>
    /// <typeparam name="TExpr">Backend expression type.</typeparam>
    /// <typeparam name="TStmt">Backend statement type.</typeparam>
    /// <typeparam name="TParam">Backend parameter type.</typeparam>
    /// <param name="builder">The execution plan builder to lower to.</param>
    /// <returns>The lowered expression value.</returns>
    public virtual TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder) => throw new NotSupportedException($"{GetType().Name} does not implement Evaluate.");
}