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
        var returnType = ResolveReturnType(lambda.Body, analysis);
        var delegateType = Expression.GetDelegateType(
            [.. paramTypes, returnType]);

        var inner = CompileToDelegate(lambda.Body, lambda.Parameters, delegateType, analysis);

        return BuildCallSiteDelegate(inner, lambda.Parameters.Count, paramTypes, returnType);
    }

    private static CallSiteDelegate CompileMethod(MethodDefinitionNode method, AnalysisResult analysis) {
        var body = method.Body ?? method;
        var paramTypes = (method.Parameters ?? [])
            .Select(p => ResolveClrType(p.TypeReference, analysis) ?? typeof(object))
            .ToArray();

        // MethodDefinitionNode doesn't have a closure slot — user params start at 0
        var returnType = ResolveReturnType(body, analysis);
        var delegateType = Expression.GetDelegateType(
            [.. paramTypes, returnType]);

        var inner = CompileToDelegate(body, method.Parameters ?? [], delegateType, analysis);

        // For methods, baseOff = sp - paramCount (no closure at 0)
        return BuildCallSiteDelegate(inner, paramTypes.Length, paramTypes, returnType, hasClosure: false);
    }

    private static Delegate CompileToDelegate(Node body, IReadOnlyList<Parameter> parameters, Type delegateType, AnalysisResult analysis) {
        var gen = new LinqExpressionGenerator(analysis);
        var method = typeof(LinqExpressionGenerator).GetMethods()
            .First(m => m.Name == nameof(LinqExpressionGenerator.CompileAsDelegate)
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == typeof(Node)
                && m.GetParameters()[1].ParameterType == typeof(Parameter[]))
            .MakeGenericMethod(delegateType);
        return (Delegate)method.Invoke(gen, [body, parameters.ToArray()])!;
    }

    private static CallSiteDelegate BuildCallSiteDelegate(Delegate inner, int paramCount, Type[] paramTypes, Type returnType, bool hasClosure = true) {
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

        // Build a CallSiteDelegate for the loop body, wrapped in LoopBodyDelegate.
        // This correctly handles mutable locals because the delegate reads/writes
        // through VmState (same as CallExternal).
        return BuildLoopBodyWrapper(entry, analysis);
    }

    private static LoopBodyDelegate BuildLoopBodyWrapper(LoopBodyEntry entry, AnalysisResult analysis) {
        var gen = new LinqExpressionGenerator(analysis);
        var bodyExpr = gen.Compile(entry.BodyNode).Expression;

        // Collect free ParameterExpressions
        var collector = new ParameterCollector();
        collector.Visit(bodyExpr);

        // Build CallSiteDelegate-style wrapper: read slots → compute → write back
        var s = Expression.Parameter(typeof(VmState), "s");
        var slots = Expression.Property(Expression.Property(s, "Stack"), "RawSlots");
        var fb = Expression.Property(s, "FrameBase");
        var cas = Expression.Property(s, "CachedArgSlots");
        var localBase = Expression.Add(fb, Expression.Constant(Vm.FrameHeaderSlots));
        var paramBase = Expression.Subtract(fb, cas);

        var tempVars = new List<ParameterExpression>();
        var body = new List<Expression>();
        var readBack = new List<Expression>();

        // Build mapping of original Parameter → temp Parameter, with read + write-back
        var originalToTemp = new Dictionary<ParameterExpression, Expression>();
        foreach (var orig in collector.Parameters) {
            Expression slotAccess;
            if (orig.Name is not null && entry.LocalIndexMap?.TryGetValue(orig.Name, out int lSlot) == true)
                slotAccess = Expression.ArrayAccess(slots, Expression.Add(localBase, Expression.Constant(lSlot)));
            else if (orig.Name is not null && entry.ParamIndexMap?.TryGetValue(orig.Name, out int pSlot) == true)
                slotAccess = Expression.ArrayAccess(slots, Expression.Add(paramBase, Expression.Constant(pSlot)));
            else
                continue; // skip variables we don't know about

            // Build: <type> temp = (<type>)slot;
            //         ... compute using temp ...
            //         slot = (long)temp;
            var temp = Expression.Variable(orig.Type, orig.Name);
            tempVars.Add(temp);
            originalToTemp[orig] = temp;
            body.Add(Expression.Assign(temp, Expression.Convert(slotAccess, orig.Type)));
            readBack.Add(Expression.Assign(slotAccess, Expression.Convert(temp, typeof(long))));
        }

        // Substitute original ParameterExpressions with temp ParameterExpressions in the body
        var substituted = originalToTemp.Count > 0
            ? new ParameterSubstitutor(originalToTemp).Visit(bodyExpr)
            : bodyExpr;

        // Execute, write back, return Normal
        body.Add(substituted);
        body.AddRange(readBack);
        body.Add(Expression.Constant(LoopResult.Normal));

        return Expression.Lambda<LoopBodyDelegate>(
            Expression.Block([.. tempVars], body), s).Compile();
    }

    private sealed class ParameterCollector : ExpressionVisitor {
        public readonly HashSet<ParameterExpression> Parameters = [];
        protected override Expression VisitParameter(ParameterExpression node) { Parameters.Add(node); return node; }
    }

    private sealed class ParameterSubstitutor(Dictionary<ParameterExpression, Expression> map) : ExpressionVisitor {
        protected override Expression VisitParameter(ParameterExpression node) =>
            map.TryGetValue(node, out var replacement) ? replacement : base.VisitParameter(node);
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

    private static Type ResolveReturnType(Node body, AnalysisResult analysis) {
        var resolved = analysis.GetResolvedType(body);
        return resolved is ClrTypeDefinition clr
            ? clr.RuntimeType
            : typeof(object);
    }

    private static object? ResolveHeapObject(VmState state, int handle) =>
        handle >= 0 && handle < state.Heap.Count ? state.Heap.UnsafeGet(handle) : (object?)(long)handle;
}