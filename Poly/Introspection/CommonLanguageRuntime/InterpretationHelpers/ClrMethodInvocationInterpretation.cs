using Poly.Interpretation;

namespace Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

/// <summary>
/// Represents a method invocation in an interpretation tree.
/// </summary>
/// <remarks>
/// Wraps a CLR method call with an instance and arguments, compiling to a 
/// <see cref="System.Linq.Expressions.MethodCallExpression"/>. Handles both
/// instance and static method invocations. For static methods, the instance
/// should be a literal null value.
/// </remarks>
internal sealed class ClrMethodInvocationInterpretation(ClrMethod method, Value instance, params IEnumerable<Value> arguments) : Value {
    /// <summary>
    /// Gets the instance on which the method is invoked.
    /// </summary>
    /// <remarks>
    /// For static methods, this should be a <see cref="Literal"/> containing null.
    /// </remarks>
    public Value Instance { get; init; } = instance ?? throw new ArgumentNullException(nameof(instance));
    
    /// <summary>
    /// Gets the CLR method to be invoked.
    /// </summary>
    public ClrMethod Method { get; init; } = method ?? throw new ArgumentNullException(nameof(method));
    
    /// <summary>
    /// Gets the arguments to pass to the method.
    /// </summary>
    public Value[] Arguments { get; init; } = arguments?.ToArray() ?? throw new ArgumentNullException(nameof(arguments));

    /// <inheritdoc />
    /// <returns>The return type of the method.</returns>
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) => Method.MemberType;

    /// <inheritdoc />
    /// <remarks>
    /// For static methods, the instance expression is ignored and set to null to properly invoke the static method.
    /// </remarks>
    public override Expression BuildExpression(InterpretationContext context) {
        var instanceExpression = Instance.BuildExpression(context);
        var argumentExpressions = Arguments.Select(arg => arg.BuildExpression(context)).ToArray();

        // For static methods, always use null as the instance regardless of what was provided
        if (Method.MethodInfo.IsStatic) {
            instanceExpression = null;
        }

        return Expression.Call(instanceExpression, Method.MethodInfo, argumentExpressions);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Instance}.{Method.Name}({string.Join(", ", Arguments.Select(e => e.ToString()))})";
}