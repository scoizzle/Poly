using Poly.Interpretation.AbstractSyntaxTree;

namespace Poly.Interpretation;

/// <summary>
/// Represents middleware in the transformation pipeline.
/// Middleware can inspect, modify, or enhance nodes before passing to the next stage.
/// </summary>
public interface ITransformationMiddleware<TResult>
{
    /// <summary>
    /// Transforms a node, potentially enriching it before passing to the next middleware.
    /// </summary>
    /// <param name="context">The interpretation context.</param>
    /// <param name="node">The AST node to transform.</param>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <returns>The transformation result.</returns>
    TResult Transform(InterpretationContext context, Node node, TransformationDelegate<TResult> next);
}
