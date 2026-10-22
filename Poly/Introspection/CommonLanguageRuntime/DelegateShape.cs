using System.Linq.Expressions;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The one classification of the CLR shapes a lambda can be bridged into: a closed
/// delegate (its <c>Invoke</c> is the call signature) or
/// <see cref="Expression{TDelegate}"/> over such a delegate. The analysis resolver and
/// the emitter both ask this type, so the shape knowledge exists once.
/// </summary>
internal static class DelegateShape {
    /// <summary>True for a closed delegate type, excluding the
    /// <see cref="Delegate"/> and <see cref="MulticastDelegate"/> bases.</summary>
    public static bool IsDelegate(Type? type) =>
        type is not null
        && type != typeof(Delegate)
        && type != typeof(MulticastDelegate)
        && typeof(Delegate).IsAssignableFrom(type)
        && type.GetMethod(nameof(Action.Invoke)) is not null;

    /// <summary>True for <see cref="Expression{TDelegate}"/> over a closed delegate;
    /// yields the delegate type.</summary>
    public static bool IsExpressionOfDelegate(Type? type, out Type delegateType) {
        delegateType = null!;
        if (type is null
            || !type.IsGenericType
            || type.GetGenericTypeDefinition() != typeof(Expression<>))
            return false;
        var argument = type.GetGenericArguments()[0];
        if (!IsDelegate(argument))
            return false;
        delegateType = argument;
        return true;
    }

    /// <summary>Classify a parameter type as a delegate or <c>Expression&lt;D&gt;</c>.
    /// <paramref name="isExpressionOf"/> distinguishes the two.</summary>
    public static bool TryGet(Type? type, out Type delegateType, out bool isExpressionOf) {
        if (IsDelegate(type)) {
            delegateType = type!;
            isExpressionOf = false;
            return true;
        }
        if (IsExpressionOfDelegate(type, out delegateType)) {
            isExpressionOf = true;
            return true;
        }
        isExpressionOf = false;
        return false;
    }

    /// <summary>True for either bridgeable shape.</summary>
    public static bool IsDelegateOrExpression(Type? type) => TryGet(type, out _, out _);
}