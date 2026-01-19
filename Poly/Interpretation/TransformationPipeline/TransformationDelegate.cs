using Poly.Interpretation.AbstractSyntaxTree;

namespace Poly.Interpretation;

/// <summary>
/// Represents a transformation operation in the middleware pipeline.
/// </summary>
/// <param name="context">The interpretation context containing type information and state.</param>
/// <param name="node">The AST node to transform.</param>
/// <returns>The transformation result.</returns>
public delegate TResult TransformationDelegate<TResult>(InterpretationContext context, Node node);
