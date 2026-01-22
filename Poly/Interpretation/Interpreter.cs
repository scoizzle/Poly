namespace Poly.Interpretation;

/// <summary>
/// Orchestrates the middleware pipeline and executes AST transformations.
/// </summary>
public sealed class Interpreter<TResult> : IInterpreterResultProvider<TResult> {
    private readonly ITypeDefinitionProvider _typeProvider;
    private readonly List<ITransformationMiddleware<TResult>> _middlewares;

    internal Interpreter(
        ITypeDefinitionProvider typeProvider,
        List<ITransformationMiddleware<TResult>> middlewares)
    {
        _typeProvider = typeProvider;
        _middlewares = middlewares;
    }

    public InterpretationContext<TResult> With(Action<InterpretationContext<TResult>> contextInitializer)
    {
        ArgumentNullException.ThrowIfNull(contextInitializer);

        var pipeline = BuildPipeline();
        var context = new InterpretationContext<TResult>(_typeProvider, pipeline);
        contextInitializer(context);
        return context;
    }

    /// <summary>
    /// Interprets an AST node by running it through the configured middleware pipeline.
    /// </summary>
    public InterpretationResult<TResult> Interpret(Node root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var pipeline = BuildPipeline();
        var context = new InterpretationContext<TResult>(_typeProvider, pipeline);
        var result = pipeline(context, root);
        return new InterpretationResult<TResult>(context, result);
    }

    private TransformationDelegate<TResult> BuildPipeline()
    {
        TransformationDelegate<TResult> pipeline = null!;
        TransformationDelegate<TResult> next = (ctx, node) =>
            throw new InvalidOperationException("No middleware handled this node.");

        // Build the pipeline in reverse order so middleware chains correctly
        for (int i = _middlewares.Count - 1; i >= 0; i--) {
            var middleware = _middlewares[i];
            var nextDelegate = next;
            next = (ctx, node) => middleware.Transform(ctx, node, nextDelegate);
        }

        pipeline = next;
        return pipeline;
    }
}
