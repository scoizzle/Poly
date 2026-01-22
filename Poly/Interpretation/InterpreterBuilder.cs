using Poly.Interpretation.TransformationPipeline;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation;

/// <summary>
/// Fluent builder for configuring a middleware pipeline and creating an Interpreter.
/// </summary>
public sealed class InterpreterBuilder<TResult>
{
    private readonly ITypeDefinitionProvider _typeProvider;
    private readonly List<ITransformationMiddleware<TResult>> _middlewares = new();

    public InterpreterBuilder()
    {
        _typeProvider = ClrTypeDefinitionRegistry.Shared;
    }

    public InterpreterBuilder(ITypeDefinitionProvider typeProvider)
    {
        _typeProvider = typeProvider ?? throw new ArgumentNullException(nameof(typeProvider));
    }

    /// <summary>
    /// Adds a middleware to the pipeline.
    /// </summary>
    public InterpreterBuilder<TResult> Use(ITransformationMiddleware<TResult> middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// Adds a middleware to the pipeline using a delegate.
    /// </summary>
    /// <param name="transformFunc">The transformation function delegate.</param>
    /// <returns></returns>
    public InterpreterBuilder<TResult> Use(Func<InterpretationContext<TResult>, Node, TransformationDelegate<TResult>, TResult> transformFunc)
    {
        return Use(new DelegateTransformationMiddleware<TResult>(transformFunc));
    }

    /// <summary>
    /// Builds the Interpreter with the configured middleware pipeline.
    /// </summary>
    public Interpreter<TResult> Build()
    {
        return new Interpreter<TResult>(_typeProvider, _middlewares);
    }
}
