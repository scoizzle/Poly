using System.Linq.Expressions;
using System.Reflection;

using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Members an awaitable binds to. Well-known <c>Task</c> / <c>ValueTask</c> shapes
/// leave <see cref="IsCompleted"/> null because their <c>GetResult</c> blocks.
/// Any other shape must expose <c>IsCompleted</c> and <c>OnCompleted</c> /
/// <c>UnsafeOnCompleted</c>, and the caller waits before <c>GetResult</c>.
/// </summary>
internal sealed class AwaitPatternBinding {
    public required MethodInfo GetAwaiter { get; init; }
    public required MethodInfo GetResult { get; init; }
    public PropertyInfo? IsCompleted { get; init; }
    public MethodInfo? NotifyCompleted { get; init; }
}

/// <summary>Resolves the await pattern from a type definition. Declared members win,
/// so <c>Task&lt;T&gt;.GetAwaiter</c> is chosen over the inherited <c>Task.GetAwaiter</c>.</summary>
internal static class AwaitPattern {
    private static readonly MethodInfo WaitIfIncompleteInfo = ((MethodCallExpression)
        ((Expression<Action<Action<Action>, Func<bool>>>)((register, done) =>
            WaitIfIncomplete(register, done))).Body).Method;

    public static AwaitPatternBinding? TryBind(ITypeDefinition operandType) {
        var getAwaiter = ParameterlessInstance(operandType, "GetAwaiter");
        if (getAwaiter is null)
            return null;

        var awaiterType = getAwaiter.MemberTypeDefinition;
        var getResult = ParameterlessInstance(awaiterType, "GetResult");
        if (getResult is null)
            return null;

        if (IsWellKnown(operandType.GetRuntimeType())) {
            return new AwaitPatternBinding {
                GetAwaiter = getAwaiter.MethodInfo,
                GetResult = getResult.MethodInfo,
            };
        }

        var isCompleted = awaiterType.Properties
            .WithName("IsCompleted")
            .OfType<ClrTypeProperty>()
            .Select(property => property.PropertyInfo)
            .FirstOrDefault(property => property.PropertyType == typeof(bool)
                && property.GetIndexParameters().Length == 0
                && property.GetGetMethod() is not null);
        var notify = CompletionMethod(awaiterType);
        if (isCompleted is null || notify is null)
            return null;

        return new AwaitPatternBinding {
            GetAwaiter = getAwaiter.MethodInfo,
            GetResult = getResult.MethodInfo,
            IsCompleted = isCompleted,
            NotifyCompleted = notify,
        };
    }

    /// <summary>Expression whose value is <c>GetResult()</c>. A structural awaitable
    /// blocks on its continuation first. The awaiter stays in one variable so a
    /// struct awaiter is not copied between <c>IsCompleted</c>, the continuation, and <c>GetResult</c>.</summary>
    public static Expression CompileGetResult(Expression operand, AwaitPatternBinding binding) {
        if (binding.IsCompleted is null)
            return Call(Call(operand, binding.GetAwaiter), binding.GetResult);

        var awaiter = Variable(binding.GetAwaiter.ReturnType, "awaiter");
        var continuation = Parameter(typeof(Action), "continuation");
        var register = Lambda<Action<Action>>(
            Call(awaiter, binding.NotifyCompleted!, continuation),
            continuation);
        var done = Lambda<Func<bool>>(Property(awaiter, binding.IsCompleted));
        return Block(
            [awaiter],
            Assign(awaiter, Call(operand, binding.GetAwaiter)),
            Call(WaitIfIncompleteInfo, register, done),
            Call(awaiter, binding.GetResult));
    }

    /// <summary>Block until <paramref name="isCompleted"/> or the continuation runs.
    /// The sync context is cleared so <c>OnCompleted</c> cannot post back onto this thread.</summary>
    private static void WaitIfIncomplete(Action<Action> registerContinuation, Func<bool> isCompleted) {
        if (isCompleted())
            return;

        using var gate = new ManualResetEventSlim(false);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try {
            registerContinuation(() => gate.Set());
        }
        finally {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        if (!isCompleted())
            gate.Wait();
    }

    private static bool IsWellKnown(Type? runtime) =>
        runtime == typeof(Task)
        || runtime == typeof(ValueTask)
        || (runtime is { IsGenericType: true } && (
            runtime.GetGenericTypeDefinition() == typeof(Task<>)
            || runtime.GetGenericTypeDefinition() == typeof(ValueTask<>)));

    private static ClrMethod? ParameterlessInstance(ITypeDefinition type, string name) =>
        type.Methods
            .WithName(name)
            .WithParameterTypes()
            .OfType<ClrMethod>()
            .FirstOrDefault(method => !method.MethodInfo.IsStatic);

    private static MethodInfo? CompletionMethod(ITypeDefinition awaiterType) {
        foreach (var name in new[] { "UnsafeOnCompleted", "OnCompleted" }) {
            foreach (var method in awaiterType.Methods.WithName(name).OfType<ClrMethod>()) {
                if (method.MethodInfo.IsStatic)
                    continue;
                var parameters = method.MethodInfo.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Action))
                    return method.MethodInfo;
            }
        }
        return null;
    }
}