using System.Linq.Expressions;
using System.Reflection;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>Obtain <see cref="MethodInfo"/> and <see cref="PropertyInfo"/>
/// via C# expression tree lambdas, creating direct IL metadata references
/// that dead-member analysis can discover.</summary>
internal static class MemberHelper {
    /// <summary>Capture a void-returning method's <see cref="MethodInfo"/>
    /// via a compile-time-safe expression tree lambda.</summary>
    public static MethodInfo MethodOf(Expression<Action> expr) =>
        ((MethodCallExpression)expr.Body).Method;

    /// <summary>Capture a value-returning method's <see cref="MethodInfo"/>
    /// via a compile-time-safe expression tree lambda.</summary>
    public static MethodInfo MethodOf<T>(Expression<Func<T>> expr) =>
        ((MethodCallExpression)expr.Body).Method;

    /// <summary>Capture a <see cref="PropertyInfo"/> via a compile-time-safe
    /// expression tree lambda.</summary>
    public static PropertyInfo PropertyOf<T>(Expression<Func<T>> expr) =>
        (PropertyInfo)((MemberExpression)expr.Body).Member;
}