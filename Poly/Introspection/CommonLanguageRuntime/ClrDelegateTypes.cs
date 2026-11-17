namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR delegate type a lambda with a given parameter list and return type is: <c>Action</c> or <c>Func</c>. An
/// execution-shape fact, so it lives with the host, not in the analysis that states the lambda's meaning.
/// </summary>
internal static class ClrDelegateTypes {
    /// <summary>The CLR delegate type for <paramref name="parameters"/> and <paramref name="returnType"/>.</summary>
    /// <exception cref="ArgumentException">The CLR has no <c>Action</c>/<c>Func</c> with that many parameters.</exception>
    public static Type Of(Type[] parameters, Type? returnType) {
        ArgumentNullException.ThrowIfNull(parameters);
        if (returnType is null || returnType == typeof(void)) {
            return parameters.Length == 0
                ? typeof(Action)
                : System.Linq.Expressions.Expression.GetActionType(parameters);
        }
        var funcArgs = new Type[parameters.Length + 1];
        parameters.CopyTo(funcArgs, 0);
        funcArgs[^1] = returnType;
        return System.Linq.Expressions.Expression.GetFuncType(funcArgs);
    }
}