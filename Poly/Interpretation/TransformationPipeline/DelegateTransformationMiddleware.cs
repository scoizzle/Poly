namespace Poly.Interpretation.TransformationPipeline;

internal class DelegateTransformationMiddleware<TResult> : ITransformationMiddleware<TResult> {
    private readonly Func<InterpretationContext<TResult>, Node, TransformationDelegate<TResult>, TResult> _transformFunc;

    public DelegateTransformationMiddleware(Func<InterpretationContext<TResult>, Node, TransformationDelegate<TResult>, TResult> transformFunc)
    {
        _transformFunc = transformFunc ?? throw new ArgumentNullException(nameof(transformFunc));
    }

    public TResult Transform(InterpretationContext<TResult> context, Node node, TransformationDelegate<TResult> next)
    {
        return _transformFunc(context, node, next);
    }
}