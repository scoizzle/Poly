using Poly.Interpretation.AbstractSyntaxTree;

namespace Poly.Interpretation.Middleware;

/// <summary>
/// Terminal middleware that delegates to an injected ITransformer.
/// Custom transformers (registry) can short-circuit before reaching this.
/// </summary>
public sealed class TerminalTransformMiddleware<TResult> : ITransformationMiddleware<TResult>
{
    private readonly ITransformer<TResult> _transformer;

    public TerminalTransformMiddleware(ITransformer<TResult> transformer)
    {
        _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
    }

    public TResult Transform(InterpretationContext context, Node node, TransformationDelegate<TResult> next)
    {
        // If a custom transformer handled it, we never get called.
        // Otherwise, delegate to the final transformer to produce the result.
        // The node must be dispatched to the appropriate overload via the transformer's interface.
        return node.Transform(_transformer);
    }
}
