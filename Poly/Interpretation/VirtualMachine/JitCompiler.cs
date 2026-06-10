using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.VirtualMachine;

internal static class JitCompiler {
    /// <summary>Matches Vm.JitThreshold — number of invocations before JIT compilation.</summary>
    internal const int Threshold = 10;

    public static CallSiteDelegate Compile(FunctionEntry entry, AnalysisResult analysis) {
        if (entry.SourceNode is Lambda lambda)
            return CompileLambda(lambda, analysis);
        if (entry.SourceNode is MethodDefinitionNode method)
            return CompileMethod(method, analysis);
        throw new InvalidOperationException($"JIT not supported for {entry.SourceNode?.GetType().Name}");
    }

    private static CallSiteDelegate CompileLambda(Lambda lambda, AnalysisResult analysis) {
        var paramTypes = lambda.Parameters
            .Select(p => ResolveClrType(p.TypeReference, analysis) ?? typeof(object))
            .ToArray();
        var delegateType = Expression.GetDelegateType(
            [.. paramTypes, typeof(object)]);

        var inner = CompileToDelegate(lambda.Body, lambda.Parameters, delegateType, analysis);

        return BuildCallSiteDelegate(inner, lambda.Parameters.Count, paramTypes);
    }

    private static CallSiteDelegate CompileMethod(MethodDefinitionNode method, AnalysisResult analysis) {
        var body = method.Body ?? method;
        var paramTypes = (method.Parameters ?? [])
            .Select(p => ResolveClrType(p.TypeReference, analysis) ?? typeof(object))
            .ToArray();

        // MethodDefinitionNode doesn't have a closure slot — user params start at 0
        var delegateType = Expression.GetDelegateType(
            [.. paramTypes, typeof(object)]);

        var inner = CompileToDelegate(body, method.Parameters ?? [], delegateType, analysis);

        // For methods, baseOff = sp - paramCount (no closure at 0)
        return BuildCallSiteDelegate(inner, paramTypes.Length, paramTypes, hasClosure: false);
    }

    private static Delegate CompileToDelegate(Node body, IReadOnlyList<Parameter> parameters, Type delegateType, AnalysisResult analysis) {
        var gen = new LinqExpressionGenerator(analysis);
        var method = typeof(LinqExpressionGenerator).GetMethods()
            .First(m => m.Name == nameof(LinqExpressionGenerator.CompileAsDelegate)
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == typeof(Node)
                && m.GetParameters()[1].ParameterType == typeof(Parameter[]))
            .MakeGenericMethod(delegateType);
        return (Delegate)method.Invoke(gen, [body, parameters.ToArray()])!;
    }

    private static CallSiteDelegate BuildCallSiteDelegate(Delegate inner, int paramCount, Type[] paramTypes, bool hasClosure = true) {
        return (VmState state) => {
            if (state.DebugMode) { state.JITFallbackRequested = true; return; }
            var slots = state.Stack.RawSlots;

            // baseOff = sp - paramCount - (hasClosure ? 1 : 0)
            int sp = state.Stack.SP;
            int closureSlot = hasClosure ? 1 : 0;
            int baseOff = sp - paramCount - closureSlot;

            var args = new object?[paramCount];
            for (int i = 0; i < paramCount; i++) {
                long raw = slots[baseOff + i + closureSlot];
                args[i] = raw switch {
                    _ when paramTypes[i] == typeof(int) => (int)raw,
                    _ when paramTypes[i] == typeof(long) => raw,
                    _ when paramTypes[i] == typeof(short) => (short)raw,
                    _ when paramTypes[i] == typeof(byte) => (byte)raw,
                    _ when paramTypes[i] == typeof(bool) => raw != 0,
                    _ when paramTypes[i] == typeof(double) => BitConverter.Int64BitsToDouble(raw),
                    _ when paramTypes[i] == typeof(float) => (float)BitConverter.Int64BitsToDouble(raw),
                    _ when paramTypes[i] == typeof(uint) => (uint)raw,
                    _ when paramTypes[i] == typeof(object) || paramTypes[i].IsClass || paramTypes[i].IsInterface
                        => ResolveHeapObject(state, (int)raw),
                    _ => raw
                };
            }

            var result = inner.DynamicInvoke(args);

            // Write result to baseOff (overwriting closure/arg0)
            long resultLong = result switch {
                long l => l,
                int i => i,
                short s => s,
                byte b => b,
                bool bv => bv ? 1L : 0L,
                double d => BitConverter.DoubleToInt64Bits(d),
                float f => BitConverter.DoubleToInt64Bits(f),
                uint ui => ui,
                null => 0L,
                _ => state.Heap.Allocate(result)
            };
            slots[baseOff] = resultLong;
            state.Stack.SetSP(baseOff + 1);
        };
    }

    public static LoopBodyDelegate CompileLoopBody(LoopBodyEntry entry, AnalysisResult analysis) {
        if (analysis is null) return null!;

        var gen = new LinqExpressionGenerator(analysis);
        var compileResult = gen.Compile(entry.BodyNode);
        var bodyExpr = compileResult.Expression;
        var freeVars = compileResult.Parameters;

        // Single Expression<LoopBodyDelegate> that reads VM stack directly
        var s = Expression.Parameter(typeof(VmState), "s");
        var stack = Expression.Property(s, "Stack");
        var slots = Expression.Property(stack, "RawSlots");
        var fbExpr = Expression.Property(s, "FrameBase");
        var casExpr = Expression.Property(s, "CachedArgSlots");

        // localBase = FrameBase + FrameHeaderSlots (4)
        // paramBase = FrameBase - CachedArgSlots
        var localBase = Expression.Add(fbExpr, Expression.Constant(Vm.FrameHeaderSlots));
        var paramBase = Expression.Subtract(fbExpr, casExpr);

        var body = new List<Expression>();
        var argExprs = new Expression[freeVars.Count];

        for (int i = 0; i < freeVars.Count; i++) {
            var p = freeVars[i];
            Expression rawRead;

            if (p.Name is not null && entry.LocalIndexMap?.TryGetValue(p.Name, out int lSlot) == true) {
                rawRead = Expression.ArrayIndex(slots, Expression.Add(localBase, Expression.Constant(lSlot)));
            }
            else if (p.Name is not null && entry.ParamIndexMap?.TryGetValue(p.Name, out int pSlot) == true) {
                rawRead = Expression.ArrayIndex(slots, Expression.Add(paramBase, Expression.Constant(pSlot)));
            }
            else {
                rawRead = Expression.Constant(0L);
            }

            argExprs[i] = ConvertSlotToExpr(rawRead, p.Type);
        }

        // Call the compiled body expression with the resolved arguments
        Expression invokeBody = freeVars.Count > 0
            ? Expression.Invoke(bodyExpr, argExprs)
            : bodyExpr;

        var debugCheck = Expression.Property(s, "DebugMode");
        var fallback = Expression.Assign(Expression.Property(s, "JITFallbackRequested"), Expression.Constant(true));

        body.Add(Expression.IfThen(debugCheck, Expression.Block(fallback, Expression.Return(Expression.Label()))));
        body.Add(invokeBody);
        body.Add(Expression.Constant(LoopResult.Normal));

        return Expression.Lambda<LoopBodyDelegate>(
            Expression.Block(body), s).Compile();
    }

    private static Expression ConvertSlotToExpr(Expression rawLong, Type targetType) {
        if (targetType == typeof(long)) return rawLong;
        if (targetType == typeof(int)) return Expression.Convert(rawLong, typeof(int));
        if (targetType == typeof(short)) return Expression.Convert(rawLong, typeof(short));
        if (targetType == typeof(byte)) return Expression.Convert(rawLong, typeof(byte));
        if (targetType == typeof(bool)) return Expression.NotEqual(rawLong, Expression.Constant(0L));
        if (targetType == typeof(double)) return Expression.Convert(rawLong, typeof(double));
        if (targetType == typeof(float)) return Expression.Convert(rawLong, typeof(float));
        if (targetType == typeof(uint)) return Expression.Convert(rawLong, typeof(uint));
        return Expression.Convert(rawLong, targetType);
    }

    private static Type? ResolveClrType(Node? typeRef, AnalysisResult analysis) {
        if (typeRef is null) return null;
        var resolved = analysis.GetResolvedType(typeRef);
        return resolved is ClrTypeDefinition clr
            ? clr.RuntimeType
            : typeof(object);
    }

    private static object? ResolveHeapObject(VmState state, int handle) =>
        handle >= 0 && handle < state.Heap.Count ? state.Heap.UnsafeGet(handle) : (object?)(long)handle;
}