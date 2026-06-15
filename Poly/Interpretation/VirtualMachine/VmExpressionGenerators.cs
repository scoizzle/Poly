using System.Linq.Expressions;
using System.Numerics.Tensors;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>Expression tree generators that duplicate the logic of
/// <c>Vm.StridedBatchSet</c> and <c>Vm.CountBitsVectorized</c>
/// inline — no custom helper method calls.  These are experimental
/// alternates to the working helper-based implementations.</summary>
internal static class VmExpressionGenerators {
    // ── Reflection handles ──

    static readonly MethodInfo AsSpanMethod = typeof(MemoryExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m => m.Name == "AsSpan"
            && m.IsGenericMethod
            && m.GetParameters().Length == 3
            && m.GetParameters()[0].ParameterType.IsArray)
        .MakeGenericMethod(typeof(long));

    static readonly MethodInfo CastMethod = typeof(MemoryMarshal)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m => m.Name == "Cast" && m.IsGenericMethod
            && m.GetParameters()[0].ParameterType.IsGenericType
            && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>))
        .MakeGenericMethod(typeof(long), typeof(ulong));

    static readonly MethodInfo PopCountMethod = typeof(TensorPrimitives)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m => m.Name == nameof(TensorPrimitives.PopCount)
            && m.GetParameters().Length == 1
            && m.IsGenericMethod)
        .MakeGenericMethod(typeof(ulong));

    // ── CountBitsOp inline: TensorPrimitives.PopCount<ulong>(MemoryMarshal.Cast<long, ulong>(arr.AsSpan(0, wc))) ──

    /// <summary>Build an expression that computes the total PopCount
    /// of <c>arr[0..wc)</c> using <c>TensorPrimitives.PopCount</c>
    /// directly — no intermediate chunk buffer or separate Sum call.
    /// Stack: (none) → pushes <c>long</c> result.</summary>
    public static Expression BuildCountBitsInline(
        Expression arrExpr, Expression wcExpr, CompilationContext ctx) {
        var arr = Expression.Variable(typeof(long[]), "a");
        var wc = Expression.Variable(typeof(int), "wc2");
        var total = Expression.Variable(typeof(long), "t");
        ctx.Variables.Add(arr); ctx.Variables.Add(wc); ctx.Variables.Add(total);

        var zero = Expression.Constant(0);
        var spanExpr = Expression.Convert(
            Expression.Call(null, AsSpanMethod, arrExpr, zero, wcExpr),
            typeof(ReadOnlySpan<long>));
        var ulongSpan = Expression.Call(null, CastMethod, spanExpr);
        var pop = Expression.Call(null, PopCountMethod, ulongSpan);
        return Expression.Block(
            [arr, wc, total],
            Expression.Assign(arr, arrExpr),
            Expression.Assign(wc, wcExpr),
            Expression.Assign(total, Expression.Convert(pop, typeof(long))),
            ctx.Push(total));
    }

    // ── StridedBatchSet inline: word-batched loop in expression tree ──

    /// <summary>Build an expression that marks all composite numbers
    /// in a sieve word array using a per-word batch approach.
    /// Stack: <c>[arr_handle_or_dummy, start, step, limit]</c> → cleaned.</summary>
    public static Expression BuildStridedBatchSetInline(CompilationContext ctx) {
        var arr = Expression.Variable(typeof(long[]), "a");
        var limit = Expression.Variable(typeof(long), "lim");
        var step = Expression.Variable(typeof(long), "stp");
        var j = Expression.Variable(typeof(long), "j");
        var word = Expression.Variable(typeof(long), "w");
        var v = Expression.Variable(typeof(long), "v");
        var lastInWord = Expression.Variable(typeof(long), "last");
        ctx.Variables.Add(arr); ctx.Variables.Add(limit);
        ctx.Variables.Add(step); ctx.Variables.Add(j);
        ctx.Variables.Add(word); ctx.Variables.Add(v); ctx.Variables.Add(lastInWord);

        var setup = new List<Expression>
        {
            Expression.Assign(limit, ctx.Pop()),
            Expression.Assign(step, ctx.Pop()),
            Expression.Assign(j, ctx.Pop()),
        };

        // For alias support, the caller pushes the alias dummy and we pop it.
        // For heap path, the caller pushes the handle and we need to resolve it.
        // (Caller is responsible for getting the array into ctx — this generator
        //  just pops the remaining values and uses `arr`.)
        // For now, assume `arr` is already assigned by the caller before calling.

        // Align j to step boundary
        var rem = Expression.Variable(typeof(long), "rem");
        ctx.Variables.Add(rem);
        setup.Add(Expression.Assign(rem, Expression.Modulo(j, step)));
        setup.Add(Expression.IfThen(Expression.NotEqual(rem, Expression.Constant(0L)),
            Expression.AddAssign(j, Expression.Subtract(step, rem))));

        var wordLoop = Expression.Label("wl");
        var wordDone = Expression.Label("wd");
        var bitLoop = Expression.Label("bl");
        var done = Expression.Label("dn");

        setup.Add(Expression.Label(wordLoop));
        setup.Add(Expression.IfThen(Expression.GreaterThan(j, limit),
            Expression.Goto(done)));
        setup.Add(Expression.Assign(word, Expression.RightShift(j, Expression.Constant(6))));
        setup.Add(Expression.Assign(v, Expression.ArrayAccess(arr,
            Expression.Convert(word, typeof(int)))));
        setup.Add(Expression.Assign(lastInWord, Expression.Condition(
            Expression.LessThan(limit,
                Expression.Add(Expression.LeftShift(word, Expression.Constant(6)),
                    Expression.Constant(63L))),
            limit,
            Expression.Add(Expression.LeftShift(word, Expression.Constant(6)),
                Expression.Constant(63L)))));

        setup.Add(Expression.Label(bitLoop));
        setup.Add(Expression.IfThen(Expression.GreaterThan(j, lastInWord),
            Expression.Goto(wordDone)));
        setup.Add(Expression.Assign(v,
            Expression.Or(v, Expression.LeftShift(Expression.Constant(1L),
                Expression.Convert(Expression.And(j, Expression.Constant(63L)), typeof(int))))));
        setup.Add(Expression.AddAssign(j, step));
        setup.Add(Expression.Goto(bitLoop));

        setup.Add(Expression.Label(wordDone));
        setup.Add(Expression.Assign(Expression.ArrayAccess(arr,
            Expression.Convert(word, typeof(int))), v));
        setup.Add(Expression.Goto(wordLoop));

        setup.Add(Expression.Label(done));
        return Expression.Block(setup);
    }
}