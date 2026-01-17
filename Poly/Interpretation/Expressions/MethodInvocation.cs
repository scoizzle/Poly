namespace Poly.Interpretation.Expressions;

/// <summary>
/// Represents a method invocation on a target object.
/// </summary>
/// <param name="target">The target object on which the method is invoked.</param>
/// <param name="methodName">The name of the method to invoke.</param>
/// <param name="arguments">The arguments to pass to the method.</param>
public sealed class MethodInvocation(Interpretable target, string methodName, params IEnumerable<Interpretable> arguments) : Interpretable {
    public Interpretable Target { get; } = target;
    public string MethodName { get; } = methodName;
    public IEnumerable<Interpretable> Arguments { get; } = arguments;

    /// <inheritdoc />
    public override TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder)
    {
        var target = Target.Evaluate(builder);
        var args = Arguments.Select(arg => arg.Evaluate(builder)).ToArray();
        return builder.Call(target, MethodName, args);
    }
}