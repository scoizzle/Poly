using System.Linq.Expressions;

namespace Poly.Interpretation.LinqExpressions;

/// <summary>
/// Extensibility interface for compiling custom AST node types to LINQ expressions.
/// </summary>
/// <remarks>
/// This allows external systems (like DataModeling) to register custom compilers for their
/// domain-specific node types without the Interpretation layer needing direct references.
/// </remarks>
public interface INodeCompiler {
    /// <summary>
    /// Attempts to compile a node to a LINQ expression.
    /// </summary>
    /// <param name="node">The AST node to compile.</param>
    /// <param name="compileChild">Callback to compile child nodes using the parent generator.</param>
    /// <param name="expression">The compiled expression if successful.</param>
    /// <returns>True if the node was compiled; false if this compiler doesn't handle this node type.</returns>
    bool TryCompile(Node node, Func<Node, Expression> compileChild, out Expression? expression);
}