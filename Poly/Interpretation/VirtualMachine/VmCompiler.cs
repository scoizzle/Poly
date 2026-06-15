using System.Linq.Expressions;
using System.Reflection;

using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>
/// Compiles analyzed AST nodes into typed delegates via the VM pipeline
/// (lowering → µop compilation → VM execution).
/// </summary>
/// <remarks>
/// This is the VM equivalent of <see cref="LinqExpressions.LinqExpressionGenerator"/>.
/// It consumes an <see cref="AnalysisResult"/> and produces a compiled delegate
/// by lowering the AST to µops, compiling the µops to an <c>Action&lt;VmState&gt;</c>,
/// and wrapping that in a LINQ Expression lambda for the target delegate type.
///
/// Because the VM executes all arithmetic in <c>long</c> slots, input arguments
/// of primitive integral types are converted to <c>long</c> via numeric conversion.
/// <c>float</c> / <c>double</c> use <c>BitConverter</c>
/// (<c>DoubleToInt64Bits</c> / <c>Int64BitsToDouble</c>) to preserve bit patterns.
/// Struct and reference types are allocated on the VM heap and the handle stored
/// in the slot.
/// </remarks>
public sealed class VmCompiler {
    private readonly AnalysisResult _analysisResult;

    // Cached reflection handles for internal members
    private static readonly PropertyInfo StackProp = typeof(VmState).GetProperty(nameof(VmState.Stack))!;
    private static readonly PropertyInfo RawSlotsProp = typeof(ValueStack).GetProperty("RawSlots",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static readonly MethodInfo SetSpMethod = typeof(ValueStack).GetMethod("SetSP",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static readonly PropertyInfo CachedArgSlotsProp = typeof(VmState).GetProperty("CachedArgSlots",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static readonly PropertyInfo HeapProp = typeof(VmState).GetProperty(nameof(VmState.Heap))!;
    private static readonly MethodInfo HeapAllocate = typeof(Heap).GetMethod("Allocate",
        [typeof(object)])!;

    public VmCompiler(AnalysisResult analysisResult) {
        _analysisResult = analysisResult ?? throw new ArgumentNullException(nameof(analysisResult));
    }

    /// <summary>
    /// Compiles an AST node into a strongly-typed delegate via the VM pipeline.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type (e.g. <c>Func&lt;int, int&gt;</c>).</typeparam>
    /// <param name="node">The AST node to compile as the delegate body.</param>
    /// <param name="parameters">The Poly AST parameters matching the delegate's formal parameters.</param>
    /// <returns>A compiled and invokable delegate.</returns>
    public TDelegate CompileAsDelegate<TDelegate>(Node node, params Parameter[] parameters)
        where TDelegate : Delegate {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parameters);

        // If the node is already a MethodDefinitionNode, use it directly.
        // Otherwise wrap body + parameters in a synthetic one so the lowering
        // emits correct ReturnFromCallOp(ArgSlots) and function entries.
        var root = (node as MethodDefinitionNode)
            ?? new MethodDefinitionNode(
                "$entry",
                TypeReference.To<int>(),
                parameters,
                node is Block b ? b : new Block(node));

        var program = Lowering.Lower(root, _analysisResult);
        var compiled = program.EnsureCompiled();
        var entry = program.Functions.Count > 0 ? program.Functions[0] : null;
        int argCount = entry?.ArgSlots ?? parameters.Length;
        int localCount = entry?.LocalCount ?? 0;
        int codeLen = program.CodeLength;

        var invokeMethod = typeof(TDelegate).GetMethod("Invoke")
            ?? throw new InvalidOperationException($"Delegate type {typeof(TDelegate).Name} has no Invoke method.");
        var delegateParams = invokeMethod.GetParameters();
        var returnType = invokeMethod.ReturnType;

        // Build the wrapper expression that marshals delegate args through VmState.
        var lambdaParams = delegateParams
            .Select(p => Expression.Parameter(p.ParameterType, p.Name))
            .ToArray();

        var stateVar = Expression.Variable(typeof(VmState), "state");
        var stackExpr = Expression.Property(stateVar, StackProp);
        var slotsExpr = Expression.Property(stackExpr, RawSlotsProp);
        var heapExpr = Expression.Property(stateVar, HeapProp);

        var blockVars = new List<ParameterExpression> { stateVar };
        var bodyExprs = new List<Expression>();

        // state = new VmState { Program = program }
        bodyExprs.Add(Expression.Assign(stateVar, Expression.New(typeof(VmState))));
        bodyExprs.Add(Expression.Assign(
            Expression.Property(stateVar, nameof(VmState.Program)),
            Expression.Constant(program, typeof(Bytecode))));

        // Load each delegate argument into slots[0..N-1]
        for (int i = 0; i < argCount && i < lambdaParams.Length; i++) {
            bodyExprs.Add(Expression.Assign(
                Expression.ArrayAccess(slotsExpr, Expression.Constant(i)),
                ToSlotExpression(lambdaParams[i], heapExpr)));
        }

        // Metadata slot at [argCount]: packed (returnPC << 32) | savedFB
        long packed = ((long)codeLen << 32) | unchecked((uint)(-1));
        bodyExprs.Add(Expression.Assign(
            Expression.ArrayAccess(slotsExpr, Expression.Constant(argCount)),
            Expression.Constant(packed)));

        // Configure the initial call frame
        bodyExprs.Add(Expression.Assign(
            Expression.Property(stateVar, nameof(VmState.FrameBase)),
            Expression.Constant(0)));
        bodyExprs.Add(Expression.Assign(
            Expression.Property(stateVar, CachedArgSlotsProp),
            Expression.Constant(argCount)));
        bodyExprs.Add(Expression.Assign(
            Expression.Property(stateVar, nameof(VmState.PC)),
            Expression.Constant(entry?.PC ?? 0)));
        // SP starts after locals: args + metadata + locals = argCount + 1 + localCount
        bodyExprs.Add(Expression.Call(stackExpr, SetSpMethod,
            Expression.Constant(argCount + 1 + localCount)));

        // Execute the VM
        bodyExprs.Add(Expression.Call(typeof(Vm), nameof(Vm.Execute), null, stateVar));

        // Extract result: store in a variable before dispose so the block
        // has the correct return type.
        ParameterExpression? resultVar = null;
        if (returnType != typeof(void)) {
            resultVar = Expression.Variable(returnType, "result");
            blockVars.Add(resultVar);
            bodyExprs.Add(Expression.Assign(resultVar,
                FromSlotExpression(
                    Expression.ArrayAccess(slotsExpr, Expression.Constant(0)),
                    returnType)));
        }

        // Dispose
        bodyExprs.Add(Expression.Call(stateVar, nameof(IDisposable.Dispose), null));

        // Return the result (or void if no result)
        if (resultVar is not null)
            bodyExprs.Add(resultVar);

        var bodyBlock = Expression.Block(blockVars, bodyExprs);
        return Expression.Lambda<TDelegate>(bodyBlock, lambdaParams).Compile();
    }

    /// <summary>Convert a delegate parameter to a slot value (<c>long</c>)
    /// suitable for the VM's stack.  Non-primitive types are heap-allocated
    /// and their handle is stored.</summary>
    private static Expression ToSlotExpression(Expression value, Expression heapExpr) {
        var type = value.Type;

        if (type == typeof(long)) return value;
        if (type == typeof(ulong)) return Expression.Convert(value, typeof(long));

        if (type == typeof(int) || type == typeof(uint)
            || type == typeof(short) || type == typeof(ushort)
            || type == typeof(byte) || type == typeof(sbyte)
            || type == typeof(char))
            return Expression.Convert(value, typeof(long));

        if (type == typeof(bool))
            return Expression.Condition(value, Expression.Constant(1L), Expression.Constant(0L));

        if (type == typeof(double) || type == typeof(float)) {
            var dbl = type == typeof(float)
                ? Expression.Convert(value, typeof(double))
                : value;
            return Expression.Call(typeof(BitConverter),
                nameof(BitConverter.DoubleToInt64Bits), null, dbl);
        }

        // Non-primitive types: box (if value type) → allocate on VM heap → store handle
        var boxed = type.IsValueType
            ? Expression.Convert(value, typeof(object))
            : value;
        return Expression.Convert(
            Expression.Call(heapExpr, HeapAllocate, boxed),
            typeof(long));
    }

    /// <summary>Convert a slot value (<c>long</c>) to the target return type.</summary>
    private static Expression FromSlotExpression(Expression slotValue, Type targetType) {
        if (targetType == typeof(long) || targetType == typeof(ulong))
            return Expression.Convert(slotValue, targetType);
        if (targetType == typeof(int) || targetType == typeof(uint)
            || targetType == typeof(short) || targetType == typeof(ushort)
            || targetType == typeof(byte) || targetType == typeof(sbyte)
            || targetType == typeof(char))
            return Expression.Convert(slotValue, targetType);
        if (targetType == typeof(bool))
            return Expression.NotEqual(slotValue, Expression.Constant(0L));
        if (targetType == typeof(double) || targetType == typeof(float)) {
            var doubleExpr = Expression.Call(typeof(BitConverter),
                nameof(BitConverter.Int64BitsToDouble), null, slotValue);
            return targetType == typeof(float)
                ? Expression.Convert(doubleExpr, typeof(float))
                : doubleExpr;
        }
        return Expression.Convert(slotValue, targetType);
    }
}