namespace Poly.Interpretation;

public interface IInterpreterResultProvider<TResult>
{
    public InterpretationContext<TResult> With(Action<InterpretationContext<TResult>> contextInitializer);
    public InterpretationResult<TResult> Interpret(Node root);
}
