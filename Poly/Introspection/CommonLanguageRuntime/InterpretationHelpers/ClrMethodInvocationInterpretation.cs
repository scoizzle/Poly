using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using System.Linq.Expressions;

namespace Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

/// <summary>
/// Represents a method invocation in an interpretation tree.
/// </summary>
/// <remarks>
/// Wraps a CLR method call with an instance and arguments, compiling to a 
/// <see cref="Expression.Call"/>. Handles both
/// instance and static method invocations. For static methods, the instance
/// should be a literal null value.
/// </remarks>
internal sealed record ClrMethodInvocationInterpretation(ClrMethod Method, Node Instance, params Node[] Arguments) : Node {
    public override TResult Transform<TResult>(ITransformer<TResult> transformer)
    {
        // Special handling for Expression transformers
        if (transformer is ITransformer<Expression> exprTransformer)
        {
            var instanceExpr = Instance.Transform(exprTransformer);
            var argumentExprs = Arguments.Select(arg => arg.Transform(exprTransformer)).ToArray();
            
            var methodInfo = Method.MethodInfo;
            var callExpr = methodInfo.IsStatic
                ? Expression.Call(null, methodInfo, argumentExprs)
                : Expression.Call(instanceExpr, methodInfo, argumentExprs);
            
            return (TResult)(object)callExpr;
        }
        
        throw new NotSupportedException($"ClrMethodInvocationInterpretation transformation is not supported for {typeof(TResult).Name}.");
    }

    /// <inheritdoc />
    public override string ToString() => $"{Instance}.{Method.Name}({string.Join(", ", Arguments.Select(e => e.ToString()))})";
}