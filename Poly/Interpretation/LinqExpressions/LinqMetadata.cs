using System.Linq.Expressions;

namespace Poly.Interpretation.LinqExpressions;

/// <summary>
/// Metadata for LINQ expression generation during interpretation.
/// </summary>
public sealed class LinqMetadata {
    /// <summary>
    /// Mapping of parameter names to their corresponding ParameterExpression instances.
    /// </summary>
    public Dictionary<string, ParameterExpression> Parameters { get; } = new();
}