using Poly.Introspection;

namespace Poly.Interpretation;

/// <summary>
/// Fluent builder for configuring a middleware pipeline and creating an Interpreter.
/// </summary>
public sealed class InterpreterBuilder<TResult>
{
    private readonly ITypeDefinitionProvider _typeProvider;
    private readonly List<ITransformationMiddleware<TResult>> _middlewares = new();

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
    /// Builds the Interpreter with the configured middleware pipeline.
    /// </summary>
    public Interpreter<TResult> Build()
    {
        return new Interpreter<TResult>(_typeProvider, _middlewares);
    }
}
