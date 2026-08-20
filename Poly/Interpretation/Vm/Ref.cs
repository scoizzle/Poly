using System.Linq.Expressions;
using System.Reflection;

namespace Poly.Interpretation.Vm;

/// <summary>Compile-time-safe <see cref="MethodInfo"/>, <see cref="PropertyInfo"/>,
/// and <see cref="ConstructorInfo"/> lookups via expression tree lambdas.
/// The typed overloads let you write
/// <c>Ref&lt;VmState&gt;.Method(s =&gt; Vm.HandleCall(s, 0, 0))</c> and
/// <c>Ref&lt;VmState&gt;.Property(s =&gt; s.Stack)</c> without reflection strings
/// or BindingFlags — dead-member analysis can discover the references.</summary>
internal static class Ref {
    /// <summary>No-argument void-returning method, e.g. <c>() => foo.Bar()</c>.</summary>
    public static MethodInfo Method(Expression<Action> expr) =>
        ((MethodCallExpression)expr.Body).Method;

    /// <summary>No-argument value-returning method (boxed to object?), e.g. <c>() => foo.Bar()</c>.</summary>
    public static MethodInfo Method(Expression<Func<object?>> expr) =>
        expr.Body is MethodCallExpression mce
            ? mce.Method
            : ((MethodCallExpression)((UnaryExpression)expr.Body).Operand).Method;

    /// <summary>
    /// General-purpose method lookup for any delegate-shaped expression
    /// (static or instance, any arity and return type), e.g.
    /// <c>Ref.Method((object? a, object? b) =&gt; object.Equals(a, b))</c> or
    /// <c>Ref.Method((ulong v) =&gt; System.Numerics.BitOperations.PopCount(v))</c>.
    /// The delegate type is inferred from the lambda, so no predeclared
    /// delegate type is required.
    /// </summary>
    public static MethodInfo Method<TDelegate>(Expression<TDelegate> expr) where TDelegate : Delegate =>
        ((MethodCallExpression)expr.Body).Method;

    /// <summary>Constructor via <c>() => new T(args)</c>, e.g. <c>Ref.Constructor(() => new InvalidOperationException(""))</c>.
    /// Extract the <see cref="ConstructorInfo"/> from the <c>new</c> expression.</summary>
    public static ConstructorInfo Constructor<T>(Expression<Func<T>> expr) =>
        ((NewExpression)expr.Body).Constructor!;
}

/// <summary>Compile-time-safe member lookups for type <typeparamref name="T"/>.
/// Use <c>Ref&lt;T&gt;.Property(x =&gt; x.Foo)</c> or
/// <c>Ref&lt;T&gt;.Method(x =&gt; x.Bar())</c> instead of string-based reflection.</summary>
internal static class Ref<T> {
    /// <summary>Instance void-returning method on <typeparamref name="T"/>,
    /// e.g. <c>Ref&lt;ValueStack&gt;.Method(s =&gt; s.SetStackPointer(0))</c>.</summary>
    public static MethodInfo Method(Expression<Action<T>> expr) =>
        ((MethodCallExpression)expr.Body).Method;

    /// <summary>Instance value-returning method on <typeparamref name="T"/>
    /// (boxed to object? for value-type returns),
    /// e.g. <c>Ref&lt;Heap&gt;.Method(h =&gt; h.UnsafeGet(0))</c>.</summary>
    public static MethodInfo Method(Expression<Func<T, object?>> expr) =>
        expr.Body is MethodCallExpression mce
            ? mce.Method
            : ((MethodCallExpression)((UnaryExpression)expr.Body).Operand).Method;

    /// <summary>Instance property on <typeparamref name="T"/>,
    /// e.g. <c>Ref&lt;VmState&gt;.Property(s =&gt; s.Stack)</c>
    /// or <c>Ref&lt;ValueStack&gt;.Property(s =&gt; (object)s.StackPointer)</c>.</summary>
    public static PropertyInfo Property(Expression<Func<T, object?>> expr) =>
        expr.Body is MemberExpression me
            ? (PropertyInfo)me.Member
            : (PropertyInfo)((MemberExpression)((UnaryExpression)expr.Body).Operand).Member;
}