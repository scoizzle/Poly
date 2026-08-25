using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

public static partial class DirectVmAbiEmitter {

    /// <summary>
    /// Invoke: for Lambda targets, compile the body inline (no separate delegate).
    /// </summary>
    private static Expression EmitInvoke(Invoke invoke, AbiCtx ctx) {
        // Handle Invoke(Member(instance, "Method"), args) — resolve the method
        // and call it directly via CLR reflection, bypassing full lambda handling.
        if (invoke.Delegate is Member member) {
            var resolved = ctx.Analysis?.GetResolvedMember(member)
                ?? ctx.Analysis?.GetResolvedMember(invoke);
            var method = resolved as ITypeMethod;
            if (method is not null) {
                var methodInfo = (method as ClrMethod)?.MethodInfo;
                if (methodInfo is not null) {
                    int d = ctx.RingDepth;
                    // Compile instance (for instance methods) or null (for static)
                    bool isStatic = method.LifetimeModifier == LifetimeModifier.Static;
                    int instanceSlot = -1;
                    Expression? instanceExpr = null;
                    if (!isStatic) {
                        instanceExpr = CompileNode(member.Value, ctx);
                        instanceSlot = ctx.RingDepth - 1;
                        var foldInst = FoldResultToSlot(ref instanceSlot, d, ctx);
                        instanceExpr = Block(instanceExpr, foldInst);
                        ctx.RingDepth = instanceSlot + 1;
                    }

                    // Compile arguments — track actual result slots
                    var argExprs = new List<Expression>();
                    int[] argSlots = new int[invoke.Arguments.Length];
                    for (int i = 0; i < invoke.Arguments.Length; i++) {
                        argExprs.Add(CompileNode(invoke.Arguments[i], ctx));
                        argSlots[i] = ctx.RingDepth - 1;
                    }
                    ctx.RingDepth = d + (isStatic ? 0 : 1) + invoke.Arguments.Length + 1;

                    var methodParams = methodInfo.GetParameters();
                    var methodArgs = new Expression[methodParams.Length];
                    int baseIdx = isStatic ? 0 : 1;
                    for (int i = 0; i < methodParams.Length; i++) {
                        int slotIdx = i < invoke.Arguments.Length ? argSlots[i] : d + baseIdx + i;
                        var ringVal = ctx.RingVar(slotIdx);
                        var paramType = methodParams[i].ParameterType;
                        if (paramType.IsValueType) {
                            methodArgs[i] = AbiValueTypes.IsLongRepresentable(paramType)
                                ? Convert(ringVal, paramType)
                                : Convert(
                                    Call(ctx.HeapLocal, HeapUnsafeGet,
                                        Convert(ringVal, typeof(int))),
                                    paramType);
                        }
                        else {
                            methodArgs[i] = Convert(
                                Call(ctx.HeapLocal, HeapUnsafeGet,
                                    Convert(ringVal, typeof(int))),
                                paramType);
                        }
                    }

                    Expression callExpr;
                    if (isStatic) {
                        callExpr = Call(null, methodInfo, methodArgs);
                    }
                    else {
                        var instanceObj = Call(ctx.HeapLocal,
                            HeapUnsafeGet,
                            Convert(ctx.RingVar(instanceSlot), typeof(int)));
                        // Convert from 'object' to the declaring type so Expression.Call
                        // can resolve the method. This is a no-op cast at runtime for
                        // reference types but satisfies Expression tree type validation.
                        var declaringType = methodInfo.DeclaringType!;
                        callExpr = Call(Convert(instanceObj, declaringType), methodInfo, methodArgs);
                    }

                    int slot = ctx.AllocSlot();
                    ctx.RingDepth = slot + 1;

                    // Build the full expression sequence: instance → args → call → result
                    var fullBody = new List<Expression>();
                    if (instanceExpr is not null)
                        fullBody.Add(instanceExpr);
                    fullBody.AddRange(argExprs);

                    // Convert result to ABI (long-representable value types unboxed,
                    // reference + non-long value types heap-allocated)
                    var resultType = methodInfo.ReturnType;
                    if (resultType == typeof(void)) {
                        fullBody.Add(callExpr);
                        return Block(fullBody);
                    }
                    if (resultType.IsValueType) {
                        if (AbiValueTypes.IsLongRepresentable(resultType)) {
                            fullBody.Add(Assign(ctx.RingVar(slot), Convert(callExpr, typeof(long))));
                        }
                        else {
                            fullBody.Add(Assign(ctx.RingVar(slot),
                                Convert(Call(ctx.HeapLocal, HeapAllocate,
                                    Convert(callExpr, typeof(object))), typeof(long))));
                        }
                        return Block(fullBody);
                    }
                    // Reference type return: allocate on heap
                    fullBody.Add(Assign(ctx.RingVar(slot),
                        Convert(Call(ctx.HeapLocal, HeapAllocate,
                            Convert(callExpr, typeof(object))), typeof(long))));
                    return Block(fullBody);
                }
            }

            // ITypeMethod without MethodInfo, or Member that analysis did not
            // resolve as a method: live instance by name+arity, then
            // InvokeNamed(string, object?[]). Fail-closed inside InvokeInstanceMethod.
            int astDepth = ctx.RingDepth;
            var astInstanceExpr = CompileNode(member.Value, ctx);
            int astInstanceSlot = ctx.RingDepth - 1;
            var astFoldInst = FoldResultToSlot(ref astInstanceSlot, astDepth, ctx);
            astInstanceExpr = Block(astInstanceExpr, astFoldInst);
            ctx.RingDepth = astInstanceSlot + 1;

            var astArgExprs = new List<Expression>();
            int[] astArgSlots = new int[invoke.Arguments.Length];
            for (int i = 0; i < invoke.Arguments.Length; i++) {
                astArgExprs.Add(CompileNode(invoke.Arguments[i], ctx));
                astArgSlots[i] = ctx.RingDepth - 1;
            }

            var astInstanceObj = Call(ctx.HeapLocal, HeapUnsafeGet,
                Convert(ctx.RingVar(astInstanceSlot), typeof(int)));
            var argObjs = new Expression[invoke.Arguments.Length];
            for (int i = 0; i < invoke.Arguments.Length; i++) {
                argObjs[i] = Call(ctx.HeapLocal, HeapUnsafeGet,
                    Convert(ctx.RingVar(astArgSlots[i]), typeof(int)));
            }

            var invokeCall = Call(
                null,
                InvokeInstanceMethodInfo,
                astInstanceObj,
                Constant(member.MemberName),
                NewArrayInit(typeof(object), argObjs));

            int astSlot = ctx.AllocSlot();
            ctx.RingDepth = astSlot + 1;
            var astBody = new List<Expression>(astArgExprs.Count + 3) { astInstanceExpr };
            astBody.AddRange(astArgExprs);

            var returnClr = method?.MemberTypeDefinition.GetRuntimeType();
            bool isVoid = method is not null
                && (returnClr == typeof(void)
                    || string.Equals(method.MemberTypeDefinition.Name, "void", StringComparison.Ordinal));
            if (isVoid) {
                astBody.Add(invokeCall);
                astBody.Add(Assign(ctx.RingVar(astSlot), Constant(0L)));
            }
            else if (returnClr is not null
                && returnClr.IsValueType
                && AbiValueTypes.IsLongRepresentable(returnClr)) {
                astBody.Add(Assign(ctx.RingVar(astSlot),
                    Convert(Convert(invokeCall, returnClr), typeof(long))));
            }
            else {
                astBody.Add(Assign(ctx.RingVar(astSlot),
                    Convert(Call(ctx.HeapLocal, HeapAllocate, invokeCall), typeof(long))));
            }
            return Block(astBody);
        }

        if (invoke.Delegate is Lambda lambda) {

            // Trivial inline: no captures → compile body directly in the caller's
            // context.  Skip when the lambda has parameters but Invoke has no
            // argument expressions (SetArgs pattern): those parameters live in
            // value-stack slots and need ParamSlotOffset/FramePos setup from the
            // frame path so locals don't overwrite them on EmitScopeStores.
            if (FindCaptures(lambda.Body, ctx).Count == 0
                && !(lambda.Parameters.Count > 0 && invoke.Arguments.Length == 0)) {
                return EmitInlineInvoke(lambda, invoke, ctx);
            }

            // 0. Declare lambda parameters in a child scope so EmitParameter works.
            // Reset the parameter slot counter for this invocation level so that
            // nested functions use their own parameter index space (INT-027).
            ctx.PushScope();
            int savedNextParamSlot = ctx.SaveAndResetParamSlots();
            foreach (var param in lambda.Parameters)
                ctx.DeclareParameter(param);

            // 1. Compile the Lambda node — allocates closure on heap, result on ring
            var closureExpr = EmitLambda(lambda, ctx);
            int closureSlot = ctx.RingDepth - 1;

            // 2. Compile arguments — each goes on ring
            var argExprs = new List<Expression>();
            int[] argSlots = new int[invoke.Arguments.Length];
            for (int i = 0; i < invoke.Arguments.Length; i++) {
                argExprs.Add(CompileNode(invoke.Arguments[i], ctx));
                argSlots[i] = ctx.RingDepth - 1;
            }
            int saveDepth = ctx.RingDepth;

            var preBody = new List<Expression>();   // before inline body
            var postBody = new List<Expression>();  // after inline body
            var spProp = Property(Property(ctx.State, "Stack"), "StackPointer");
            var regSlots = Property(ctx.State, "Registers");

            // 3. Create a per-invocation SP local (avoids nested invoke corruption
            // of the shared ctx.SavedSp). Save current ring to Registers[invokeSp + k].
            var invokeSp = Variable(typeof(int), "_invokeSp");
            preBody.Add(Assign(invokeSp, spProp));
            for (int k = 0; k < saveDepth; k++)
                preBody.Add(Assign(ArrayAccess(regSlots, Add(invokeSp, Constant(k))), ctx.RingVar(k)));

            // 4. Set state.ClosureHandle from the closure slot
            preBody.Add(Assign(
                Property(ctx.State, nameof(VmState.ClosureHandle)),
                Convert(ctx.RingVar(closureSlot), typeof(int))));

            // 5. Push arguments from ring to value stack with optional 2-word frame header.
            // When there are explicit Invoke arguments, push a 2-word header (PreviousFP,
            // SavedSP) before the arguments. When there are 0 arguments (the SetArgs pattern),
            // skip the header to preserve backward compatibility with root-level SetArgs.
            //
            // Layout with header:
            //   [_callSp + 0] = PreviousFP (current _fp value)
            //   [_callSp + 1] = SavedSP
            //   [_callSp + 2] = arg0, [_callSp + 3] = arg1, ...
            //   _fp = callSp + 2 + max(args.Length, 1)
            //
            // ParameterRead uses _fp - ParamSlotOffset + paramIdx which resolves to
            // _slots[callSp + 2 + paramIdx] — correct with the 2-word header.
            var callSp = Variable(typeof(int), "_callSp");
            preBody.Add(Assign(callSp, spProp));

            int headerSize = invoke.Arguments.Length > 0 ? 2 : 0;

            if (headerSize > 0) {
                // Push PreviousFP (= current _fp as long)
                preBody.Add(Assign(ArrayAccess(ctx.SlotsLocal, callSp),
                    Convert(ctx.FramePosLocal, typeof(long))));
                // Push SavedSP (= callSp as long, the SP before the header)
                preBody.Add(Assign(ArrayAccess(ctx.SlotsLocal, Add(callSp, Constant(1))),
                    Convert(callSp, typeof(long))));
                // Advance SP past the 2-word header
                preBody.Add(Call(Property(ctx.State, "Stack"),
                    SetStackPointer,
                    Add(callSp, Constant(headerSize))));
            }

            // Push arguments
            for (int i = 0; i < invoke.Arguments.Length; i++) {
                preBody.Add(Assign(ArrayAccess(ctx.SlotsLocal,
                    Add(callSp, Constant(headerSize + i))),
                    ctx.RingVar(argSlots[i])));
            }
            // Advance SP past args
            if (invoke.Arguments.Length > 0 || headerSize > 0) {
                preBody.Add(Call(Property(ctx.State, "Stack"),
                    SetStackPointer,
                    Add(callSp, Constant(headerSize + invoke.Arguments.Length))));
            }

            // _fp = callSp + headerSize + max(args.Length, 1) so params are at slots[_fp - N + paramIdx]
            // Even with 0 args, reserve 1 slot for implicit SetArgs parameter.
            int paramCount = Math.Max(1, invoke.Arguments.Length);
            Expression newFp = Add(callSp, Constant(headerSize + paramCount));

            // 6. Set new _fp (saved PreviousFP is already in the 2-word header).
            preBody.Add(Assign(ctx.FramePosLocal, newFp));

            // Set ParamSlotOffset so EmitParameter reads from corrected slot
            int savedParamOffset = ctx.ParamSlotOffset;
            ctx.ParamSlotOffset = paramCount;

            // Compile lambda body inline — leaves result on ring
            var bodyExpr = CompileNode(lambda.Body, ctx);
            int bodyResultSlot = ctx.RingDepth - 1;
            preBody.Add(bodyExpr);
            // Save result to temp local BEFORE ring restore (use per-invocation local)
            var invokeResult = Variable(typeof(long), "_invokeResult");
            preBody.Add(Assign(invokeResult, ctx.RingVar(bodyResultSlot)));
            ctx.RingDepth = 1;

            // Restore ring from state.Registers using per-invocation invokeSp
            for (int k = 0; k < saveDepth; k++)
                postBody.Add(Assign(ctx.RingVar(k), ArrayAccess(regSlots, Add(invokeSp, Constant(k)))));

            // Write saved result to ring slot 0 (ring now fully restored)
            postBody.Add(Assign(ctx.RingVar(0), invokeResult));

            // Restore _fp from the 2-word header (PreviousFP at _slots[callSp]).
            // When headerSize == 0 (SetArgs pattern), restore from OldFramePos legacy field.
            if (headerSize > 0) {
                var prevFp = Convert(ArrayAccess(ctx.SlotsLocal, callSp), typeof(int));
                postBody.Add(Assign(ctx.FramePosLocal, prevFp));
            }
            else {
                postBody.Add(Assign(ctx.FramePosLocal,
                    Property(ctx.State, nameof(VmState.OldFramePos))));
            }

            ctx.ParamSlotOffset = savedParamOffset;  // restore
            ctx.RestoreParamSlots(savedNextParamSlot);  // restore outer param slot counter
            ctx.PopScope();
            return Block([invokeSp, callSp, invokeResult], closureExpr, Block(argExprs),
                Block(preBody.Concat(postBody)));
        }

        throw new NotSupportedException(
            $"DirectVmAbiEmitter: Invoke not supported for delegate type {invoke.Delegate.GetType().Name}");
    }

    /// <summary>Inline a small lambda with no captures and a single-expression body.
    /// Skips frame push, closure allocation, and ring save/restore — compiles
    /// arguments directly to the ring and maps parameter reads to argument slots.</summary>
    private static Expression EmitInlineInvoke(Lambda lambda, Invoke invoke, AbiCtx ctx) {
        int depth = ctx.RingDepth;
        var argExprs = new Expression[invoke.Arguments.Length];
        for (int i = 0; i < invoke.Arguments.Length; i++) {
            argExprs[i] = CompileNode(invoke.Arguments[i], ctx);
            ctx.MapInlineParameter(i, ctx.RingDepth - 1);
        }
        var bodyExpr = CompileNode(lambda.Body, ctx);
        ctx.ClearInlineParameters();
        return Block(argExprs.Concat([bodyExpr]));
    }

    /// <summary>
    /// Recursively collect all Lambda nodes from an AST subtree.
    /// </summary>
    private static void CollectLambdas(Node node, List<Lambda> result) {
        if (node is Lambda lambda) {
            lambda.LambdaIndex = result.Count; // assign index in collection order
            result.Add(lambda);
            // Still recurse into body for nested lambdas
            CollectLambdas(lambda.Body, result);
            return;
        }
        foreach (var child in node.Children) {
            if (child is not null)
                CollectLambdas(child, result);
        }
    }

    /// <summary>
    /// Compile a lambda body as a standalone <c>Action&lt;VmState&gt;</c> delegate.
    /// Parameters are read from value-stack slots.
    /// Captured variables are read from/written to <c>heap[state.ClosureHandle][captureIndex]</c>.
    /// </summary>
    private static Action<VmState> CompileFunctionBody(
        Node body,
        IReadOnlyList<Parameter> parameters,
        List<Capture> captures,
        Action<VmState>[] functionTable,
        CompilationMode mode) {

        var fnCtx = new AbiCtx();
        fnCtx.FunctionTableExpr = Constant(functionTable);
        fnCtx.Mode = mode;
        var bodyExprs = new List<Expression>();

        // Preamble
        bodyExprs.Add(Label(fnCtx.EntryLabel));
        bodyExprs.Add(Assign(fnCtx.SlotsLocal, fnCtx.SlotsInitExpression));
        bodyExprs.Add(Assign(fnCtx.HeapLocal, fnCtx.HeapInitExpression));
        bodyExprs.Add(Assign(fnCtx.Registers,
            Coalesce(fnCtx.Registers, NewArrayBounds(typeof(long), Constant(256)))));
        bodyExprs.Add(Assign(fnCtx.FramePosLocal,
            Condition(
                Equal(Property(fnCtx.State, nameof(VmState.Status)),
                    Constant(InterpreterStatus.Resuming)),
                Property(fnCtx.State, nameof(VmState.FramePos)),
                Constant(0))));

        if (mode != CompilationMode.NoDebug) {
            fnCtx.DebugHookProp = Property(fnCtx.State, nameof(VmState.DebugHook));
        }

        // Register captures so EmitVariable/EmitAssignment can route to heap reads
        for (int i = 0; i < captures.Count; i++)
            fnCtx.DeclareCapture(captures[i].Variable, i);

        // Declare parameters as value-stack variables (mapped to _slots[_fp + idx])
        fnCtx.PushScope();
        foreach (var param in parameters)
            fnCtx.DeclareParameter(param);

        // Enter the activation in the compile-time simulator.
        // Local count is 0 initially; DeclareVariable calls inside the body
        // will assign scope-relative slots but the simulator currently tracks
        // frame size based on the counts passed here.
        fnCtx.EnterActivation(parameters.Count, 0);

        // Compile body
        var bodyCompiled = CompileNode(body, fnCtx);
        bodyExprs.Add(bodyCompiled);

        // Leave the activation in the compile-time simulator.
        fnCtx.LeaveActivation();

        // Flush result: return value at _slots[_fp], SP = _fp + 1
        if (fnCtx.RingDepth > 0) {
            bodyExprs.Add(Assign(ArrayAccess(fnCtx.SlotsLocal, fnCtx.FramePosLocal),
                fnCtx.RingVar(fnCtx.RingDepth - 1)));
            bodyExprs.Add(Assign(fnCtx.SlotsStackPointer,
                Add(fnCtx.FramePosLocal, Constant(1))));
        }

        bodyExprs.Add(Label(fnCtx.ExitLabel));

        var delegateExpr = Lambda<Action<VmState>>(Block(fnCtx.Locals, bodyExprs), fnCtx.State);
        return delegateExpr.Compile();
    }

    // ── Helpers ────────────────────────────────────

    /// <summary>
    /// Wrap a compiled node expression with a simplified debug hook guard.
    /// In NoDebug mode (<see cref="AbiCtx.DebugHookProp"/> is null),
    /// returns the body unchanged — zero overhead.
    ///
    /// When a hook is set, emits code that snapshots the current frame's
    /// locals into a <c>long[]</c> buffer (using compile-time known offsets),
    /// then invokes <c>DebugHook(node, ReadOnlySpan&lt;long&gt;(buffer), heap)</c>.
    ///
    /// The <c>ProgramCounter</c> is also flushed to the current step for
    /// legacy compatibility.
    /// </summary>
    private static Expression WithInterrupt(Expression body, AbiCtx ctx, int step) {
        if (ctx.DebugHookProp is null) return body;

        int localCount = ctx.CurrentLocalCount;

        // Build the hook invocation expression only when the hook is non-null.
        // Emit: if (state.DebugHook != null) { snapshot + invoke }
        var stmts = new List<Expression>();

        // Create a ReadOnlySpan<long> directly over _slots[_fb .. _fb + localCount].
        // This is a zero-allocation slice — no buffer copy needed.
        Expression spanExpr = localCount == 0
            ? New(ReadOnlySpanLongArrayCtor,
                NewArrayBounds(typeof(long), Constant(0)))
            : New(ReadOnlySpanLongSliceCtor,
                ctx.SlotsLocal, ctx.FramePosLocal, Constant(localCount));

        stmts.Add(Invoke(ctx.DebugHookProp, ctx.CurrentAstNodeExpr, spanExpr, ctx.HeapLocal));

        return Block(
            IfThen(
                NotEqual(ctx.DebugHookProp, Constant(null, typeof(Action<Node, ReadOnlySpan<long>, Heap>))),
                Block(Assign(ctx.StatePcFlush, Constant(step)), Block(stmts))),
            body);
    }

    /// <summary>Convert a CLR constant to the long-based ABI representation.
    /// Only inline-able types; non-inline types must use <see cref="TryValueToLong"/>.</summary>

    // ── Static property/method refs ─────────────────────────────────

    private static readonly PropertyInfo StateStackProperty =
        Ref<VmState>.Property(e => e.Stack);
    private static readonly PropertyInfo ValueStackRawSlotsProperty =
        Ref<ValueStack>.Property(s => s.RawSlots);
    private static readonly PropertyInfo ValueStackStackPointerProperty =
        Ref<ValueStack>.Property(s => s.StackPointer);
    private static readonly PropertyInfo StateRegistersProperty =
        Ref<VmState>.Property(e => e.Registers);
    private static readonly PropertyInfo StateHeapRawSlotsProperty =
        Ref<VmState>.Property(e => e.Heap.RawSlots);
    private static readonly PropertyInfo StateClosureHandleProperty =
        Ref<VmState>.Property(e => e.ClosureHandle);

    // ── Float/double helpers ────────────────────────

    private static readonly MethodInfo BitConverterInt64BitsToDouble =
        Ref.Method((Expression<Func<long, double>>)(v => BitConverter.Int64BitsToDouble(v)));

    private static readonly MethodInfo BitConverterDoubleToInt64Bits =
        Ref.Method((Expression<Func<double, long>>)(v => BitConverter.DoubleToInt64Bits(v)));

    private static readonly MethodInfo InvokeInstanceMethodInfo =
        Ref.Method((Expression<Func<object, string, object?[], object?>>)((instance, name, args) =>
            InvokeInstanceMethod(instance, name, args)));

    /// <summary>
    /// Generic instance-method dispatch for Invoke(Member) whose resolved
    /// <see cref="ITypeMethod"/> has no MethodInfo (AST type defs). Looks up
    /// <paramref name="methodName"/> on the live object's type by name and
    /// arity (public + non-public instance). If missing, looks for
    /// <c>InvokeNamed(string, object?[])</c> and calls it with the method
    /// name plus args. Fail-closed if the instance is null or neither method
    /// is present.
    /// </summary>
    private static object? InvokeInstanceMethod(object instance, string methodName, object?[] args) {
        if (instance is null)
            throw new InvalidOperationException(
                $"Invoke '{methodName}' requires a non-null instance.");

        var instanceType = instance.GetType();
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo? match = null;
        foreach (var candidate in instanceType.GetMethods(flags)) {
            if (candidate.Name != methodName)
                continue;
            if (candidate.GetParameters().Length != args.Length)
                continue;
            match = candidate;
            break;
        }

        if (match is not null)
            return InvokeAndUnwrap(match, instance, args);

        // AST methods with no CLR MethodInfo (e.g. domain actions): optional
        // InvokeNamed(string, object?[]) dispatcher. Generic — no domain types.
        MethodInfo? invokeNamed = null;
        foreach (var candidate in instanceType.GetMethods(flags)) {
            if (candidate.Name != "InvokeNamed")
                continue;
            var ps = candidate.GetParameters();
            if (ps.Length != 2)
                continue;
            if (ps[0].ParameterType != typeof(string))
                continue;
            if (ps[1].ParameterType != typeof(object[]))
                continue;
            invokeNamed = candidate;
            break;
        }
        if (invokeNamed is not null)
            return InvokeAndUnwrap(invokeNamed, instance, [methodName, args]);

        throw new InvalidOperationException(
            $"Type '{instanceType.Name}' does not define method '{methodName}' with {args.Length} parameter(s).");
    }

    private static object? InvokeAndUnwrap(MethodInfo method, object instance, object?[] args) {
        try {
            return method.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

}