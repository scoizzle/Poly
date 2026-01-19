using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Introspection;

namespace Poly.Interpretation;

/// <summary>
/// Orchestrates the middleware pipeline and executes AST transformations.
/// </summary>
public sealed class Interpreter<TResult>
{
    private readonly ITypeDefinitionProvider _typeProvider;
    private readonly List<ITransformationMiddleware<TResult>> _middlewares;

    internal Interpreter(ITypeDefinitionProvider typeProvider, List<ITransformationMiddleware<TResult>> middlewares)
    {
        _typeProvider = typeProvider;
        _middlewares = middlewares;
    }

    /// <summary>
    /// Interprets an AST node by running it through the configured middleware pipeline.
    /// </summary>
    public TResult Interpret(Node root)
    {
        var context = new InterpretationContext(_typeProvider);
        var pipeline = BuildPipeline();
        return pipeline(context, root);
    }

    private TransformationDelegate<TResult> BuildPipeline()
    {
        TransformationDelegate<TResult> next = (ctx, node) =>
            throw new InvalidOperationException("No middleware handled this node.");

        // Build the pipeline in reverse order so middleware chains correctly
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var nextDelegate = next;
            next = (ctx, node) => middleware.Transform(ctx, node, nextDelegate);
        }

        return next;
    }
}
