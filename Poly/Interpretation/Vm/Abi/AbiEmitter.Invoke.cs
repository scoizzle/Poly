using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

public static partial class AbiEmitter {

    /// <summary>
    /// Invoke: for Lambda targets, compile the body inline (no separate delegate).
    /// </summary>
    private static Expression EmitInvoke(Invoke invoke, Context ctx) {
        // Handle Invoke(Member(instance, "Method"), args) — resolve the method
        // and call it directly via CLR reflection, bypassing full lambda handling.
        if (invoke.Delegate is Member member) {
            // The invoke carries the overload match. The member node keeps the
            // first same-named member, which is the wrong MethodInfo when the
            // callee is overloaded.
            var resolved = ctx.Analysis?.GetResolvedMember(invoke)
                ?? ctx.Analysis?.GetResolvedMember(member);
            var method = resolved as ITypeMethod;
            if (method is not null) {
                var methodInfo = (method as ClrMethod)?.MethodInfo;
                if (methodInfo is not null) {
                    var receiver = ctx.OpenRing();
                    // Compile instance (for instance methods) or null (for static)
                    bool isStatic = method.LifetimeModifier == LifetimeModifier.Static;
                    int instanceSlot = -1;
                    if (!isStatic) {
                        if (IsTypeNameReceiver(member.Value, ctx))
                            throw new InvalidOperationException(
                                $"Instance method '{member.MemberName}' cannot be invoked on a type name.");
                        instanceSlot = receiver.Add(CompileNode(member.Value, ctx));
                    }

                    // Compile arguments — track actual result slots
                    var args = ctx.OpenRing();
                    int[] argSlots = new int[invoke.Arguments.Length];
                    for (int i = 0; i < invoke.Arguments.Length; i++)
                        argSlots[i] = args.Add(CompileNode(invoke.Arguments[i], ctx));
                    int slot = args.Base + args.Count;
                    ctx.RingDepth = slot + 1;

                    var methodParams = methodInfo.GetParameters();
                    var methodArgs = new Expression[methodParams.Length];
                    if (TryPackEnumerableArgs(methodParams, invoke, argSlots, ctx, out var packed)) {
                        methodArgs[0] = packed;
                    }
                    else {
                        for (int i = 0; i < methodParams.Length; i++) {
                            if (i >= invoke.Arguments.Length) {
                                // Omitted optional: the default, not a ring slot (a scalar there is not a handle).
                                methodArgs[i] = MissingClrArgument(methodParams[i]);
                                continue;
                            }
                            var ringVal = ctx.RingVar(argSlots[i]);
                            methodArgs[i] = RingCodec.ToClr(
                                ringVal, methodParams[i].ParameterType, invoke.Arguments[i],
                                ctx.Analysis, ctx.HeapLocal);
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

                    // Build the full expression sequence: instance → args → call → result
                    var fullBody = new List<Expression>();
                    fullBody.AddRange(receiver.Steps);
                    fullBody.AddRange(args.Steps);

                    // A null heap receiver (unset DateOnly/DateTime) must not NRE inside AddDays.
                    if (!isStatic) {
                        var unset = UnsetReceiverGuard(member.Value, ctx.RingVar(instanceSlot), ctx);
                        if (unset is not null)
                            fullBody.Add(unset);
                    }

                    // Convert result to ABI (long-representable value types unboxed,
                    // reference + non-long value types heap-allocated)
                    var resultType = methodInfo.ReturnType;
                    if (resultType == typeof(void)) {
                        fullBody.Add(callExpr);
                        return Block(fullBody);
                    }
                    if (resultType.IsValueType) {
                        fullBody.Add(Assign(ctx.RingVar(slot),
                            RingCodec.FromClr(callExpr, resultType, ctx.HeapLocal)));
                        return Block(fullBody);
                    }
                    fullBody.Add(Assign(ctx.RingVar(slot),
                        RingCodec.FromClr(Convert(callExpr, typeof(object)), resultType, ctx.HeapLocal)));
                    return Block(fullBody);
                }

                if (method is AstMethodDefinition astMethod && HasExecutableBody(astMethod.DefinitionNode.Body)) {
                    var asyncReturn = AsyncReturnType(astMethod);
                    // C# rejects `await` in a method that is not `async` with a Task return type.
                    // Reject it here so the failure is a deterministic compile reject, not a
                    // fall-through to a CLR lookup of the same name.
                    if (asyncReturn is null && ContainsAwait(astMethod.DefinitionNode.Body!))
                        throw new InvalidOperationException(
                            $"VM compile rejected: method '{astMethod.Name}' contains 'await' but is not an async method returning Task or Task<T>.");
                    var table = TryCompileAstCallable(
                        astMethod.DefinitionNode,
                        astMethod.DefinitionNode.Body!,
                        astMethod.DefinitionNode.Parameters ?? [],
                        astMethod.DeclaringTypeDefinition,
                        ctx,
                        instanceSlot: !astMethod.IsStatic,
                        asyncReturn: asyncReturn);
                    // A body the VM cannot analyze (sibling types, host stubs) stays
                    // on the CLR / InvokeNamed path.
                    if (table is not null)
                        return EmitAstInvoke(invoke, member, astMethod, table, ctx);
                }
            }

            // ITypeMethod without MethodInfo, or Member that analysis did not
            // resolve as a method: live instance by name+arity, then
            // InvokeNamed(string, object?[]). Fail-closed inside InvokeInstanceMethod.
            if (IsTypeNameReceiver(member.Value, ctx))
                throw new InvalidOperationException(
                    $"Member '{member.MemberName}' on a type name is not a resolved static method.");

            var astReceiver = ctx.OpenRing();
            int astInstanceSlot = astReceiver.Add(CompileNode(member.Value, ctx));

            var astArgs = ctx.OpenRing();
            int[] astArgSlots = new int[invoke.Arguments.Length];
            for (int i = 0; i < invoke.Arguments.Length; i++)
                astArgSlots[i] = astArgs.Add(CompileNode(invoke.Arguments[i], ctx));

            var astInstanceObj = Call(ctx.HeapLocal, HeapUnsafeGet,
                Convert(ctx.RingVar(astInstanceSlot), typeof(int)));
            var argObjs = new Expression[invoke.Arguments.Length];
            for (int i = 0; i < invoke.Arguments.Length; i++) {
                var arg = invoke.Arguments[i];
                bool scalar = RingCodec.IsInvokeArgumentScalar(arg, ctx.Analysis);
                argObjs[i] = scalar
                    ? Convert(ctx.RingVar(astArgSlots[i]), typeof(object))
                    : Call(ctx.HeapLocal, HeapUnsafeGet,
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
            var astBody = new List<Expression>(astArgs.Count + 4);
            astBody.AddRange(astReceiver.Steps);
            astBody.AddRange(astArgs.Steps);
            var unsetReceiver = UnsetReceiverGuard(member.Value, ctx.RingVar(astInstanceSlot), ctx);
            if (unsetReceiver is not null)
                astBody.Add(unsetReceiver);

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
                && RingCodec.IsInline(returnClr)) {
                astBody.Add(Assign(ctx.RingVar(astSlot),
                    Convert(Convert(invokeCall, returnClr), typeof(long))));
            }
            else {
                // CLR/AST invoke result → ring via BoxToAbi (bool 0/1, null 0, refs handles).
                astBody.Add(Assign(ctx.RingVar(astSlot),
                    Call(null, BoxToAbiInfo, ctx.HeapLocal, invokeCall)));
            }
            return Block(astBody);
        }

        if (invoke.Delegate is Lambda lambda) {
            if (invoke.Arguments.Length > 0 && invoke.Arguments.Length != lambda.Parameters.Count)
                throw new InvalidOperationException(
                    $"VM compile rejected: lambda has {lambda.Parameters.Count} parameter(s) but invoke has {invoke.Arguments.Length} argument(s).");
            var ownParams = new HashSet<Parameter>(lambda.Parameters, ReferenceEqualityComparer.Instance);
            DeclareFreeParameters(lambda.Body, ctx, ownParams);
            foreach (var arg in invoke.Arguments) {
                if (arg is Parameter p && !ownParams.Contains(p) && ownParams.All(op => op.Name != p.Name))
                    if (!ctx.TryGetParameterSlot(p, out _))
                        ctx.DeclareParameter(p);
            }

            // Compile the body in the caller's context so captured Variables
            // read/write the same upvalue cells as the enclosing frame.
            // Skip when the lambda has parameters but Invoke has no argument
            // expressions (SetArgs pattern): those parameters live in
            // value-stack slots and need ParamSlotOffset/FramePos setup from
            // the frame path so locals don't overwrite them on EmitScopeStores.
            if (!(lambda.Parameters.Count > 0 && invoke.Arguments.Length == 0)) {
                return EmitInlineInvoke(lambda, invoke, ctx);
            }

            // A lambda whose parameters are seeded by SetArgs: compile its body inline
            // in a frame parked at the current SP. Explicit arguments take the
            // EmitInlineInvoke path above, so this branch never pushes a header or
            // argument slots of its own.
            ctx.PushScope();
            int savedNextParamSlot = ctx.SaveAndResetParamSlots();
            ctx.PushFrameParameters();
            foreach (var param in lambda.Parameters)
                ctx.MapFrameParameter(param, ctx.DeclareParameter(param));
            int saveDepth = ctx.RingDepth;

            var preBody = new List<Expression>();   // before inline body
            var postBody = new List<Expression>();  // after inline body
            var spProp = Property(Property(ctx.State, "Stack"), "StackPointer");

            // Save the caller's ring into per-invocation CLR locals. The inline body
            // may reuse these ring slots and cannot reach the locals. (A resume inside
            // the body skips this save; the root caller does not read its ring after
            // the SetArgs result is written.)
            var savedRing = new ParameterExpression[saveDepth];
            for (int k = 0; k < saveDepth; k++) {
                savedRing[k] = Variable(typeof(long), $"_save{k}");
                preBody.Add(Assign(savedRing[k], ctx.RingVar(k)));
            }

            var callSp = Variable(typeof(int), "_callSp");
            preBody.Add(Assign(callSp, spProp));
            // SetArgs seeded the arguments at slots[callSp + index]; the frame pointer
            // is that first slot, so parameters resolve at FP + index.
            preBody.Add(Assign(ctx.FramePosLocal, callSp));

            int savedParamOffset = ctx.ParamSlotOffset;
            ctx.ParamSlotOffset = 0;
            // SetArgs seeds the parameters into slots[callSp..]; reserve those slots so a
            // body local (written by EmitScopeStores) cannot overwrite a parameter.
            ctx.ReserveFrameSlots(lambda.Parameters.Count);

            // Compile lambda body inline — leaves result on ring
            var bodyExpr = CompileNode(lambda.Body, ctx);
            int bodyResultSlot = ctx.RingDepth - 1;
            preBody.Add(bodyExpr);
            // Save result to a temp local BEFORE the ring restore.
            var invokeResult = Variable(typeof(long), "_invokeResult");
            preBody.Add(Assign(invokeResult, ctx.RingVar(bodyResultSlot)));
            // The result belongs in the caller's first free slot (saveDepth), not slot 0:
            // slot 0 may hold a caller value (an earlier interpolation hole, an inline
            // parameter argument), and resetting the depth to 1 would discard the caller's
            // live slots.
            ctx.RingDepth = saveDepth + 1;

            // Restore the caller's ring from the per-invocation locals.
            for (int k = 0; k < saveDepth; k++)
                postBody.Add(Assign(ctx.RingVar(k), savedRing[k]));

            // Write saved result to the caller's result slot (ring now fully restored)
            postBody.Add(Assign(ctx.RingVar(saveDepth), invokeResult));
            // This legacy SetArgs path restores the root frame sentinel 0. The old
            // OldFramePos field was never written, so that was always the value.
            postBody.Add(Assign(ctx.FramePosLocal, Constant(0)));

            ctx.ParamSlotOffset = savedParamOffset;  // restore
            ctx.RestoreParamSlots(savedNextParamSlot);  // restore outer param slot counter
            ctx.PopFrameParameters();
            ctx.PopScope();
            var locals = new List<ParameterExpression> { callSp, invokeResult };
            locals.AddRange(savedRing);
            return Block(locals, Block(preBody.Concat(postBody)));
        }

        if (invoke.Delegate is Poly.Ast.Nodes.Variable or Poly.Ast.Nodes.Parameter)
            return EmitInvokeIndirect(invoke, ctx);

        if (invoke.Delegate is TypeReference { TypeName: "nameof" })
            throw new InvalidOperationException(
                "VM compile rejected: 'nameof' was not folded to a constant.");

        throw new InvalidOperationException(
            $"VM compile rejected: Invoke target must be a member, lambda, or stored closure, got {invoke.Delegate.GetType().Name}.");
    }

    /// <summary>
    /// Packs N compiled arguments into a <c>string[]</c> (or element array) when
    /// the callee takes a single array / <see cref="IEnumerable{T}"/> parameter.
    /// Used for flattened <c>String.Concat</c>.
    /// </summary>
    private static bool TryPackEnumerableArgs(
        ParameterInfo[] methodParams,
        Invoke invoke,
        int[] argSlots,
        Context ctx,
        out Expression packed) {
        packed = null!;
        if (methodParams.Length != 1 || invoke.Arguments.Length <= 1)
            return false;
        var paramType = methodParams[0].ParameterType;
        Type? elementType = paramType.IsArray
            ? paramType.GetElementType()
            : paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? paramType.GetGenericArguments()[0]
                : null;
        if (elementType is null)
            return false;
        var elems = new Expression[invoke.Arguments.Length];
        for (var i = 0; i < invoke.Arguments.Length; i++)
            elems[i] = RingCodec.ToClr(ctx.RingVar(argSlots[i]), elementType, invoke.Arguments[i], ctx.Analysis, ctx.HeapLocal);
        packed = NewArrayInit(elementType, elems);
        return true;
    }

    private static readonly MethodInfo StringConcatObjectArrayInfo =
        Ref.Method((Expression<Func<object?[], string>>)(parts => string.Concat(parts)));

    /// <summary>
    /// Interpolated string: <c>$"lit{hole}lit"</c>. Lowered to a single
    /// <c>string.Concat(object[])</c> call over the interleaved literals and holes.
    /// Literals become compile-time string constants; each hole is read from its ring
    /// slot and boxed as an object using its value representation — a scalar hole is
    /// converted by its CLR type, never mistaken for a heap handle the way
    /// <c>RingCodec.ToClr(..., typeof(object))</c> would (object is not a scalar kind).
    /// </summary>
    private static Expression EmitInterpolatedString(InterpolatedString node, Context ctx) {
        var parts = new List<Expression>(node.Literals.Count + node.Holes.Count);
        var seq = new List<Expression>();
        for (var i = 0; i < node.Literals.Count; i++) {
            parts.Add(Constant(node.Literals[i], typeof(object)));
            if (i >= node.Holes.Count)
                continue;
            var hole = node.Holes[i];
            var holeRing = ctx.OpenRing();
            int holeSlot = holeRing.Add(CompileNode(hole, ctx));
            parts.Add(HoleToClrObject(hole, ctx.RingVar(holeSlot), ctx));
            seq.AddRange(holeRing.Steps);
        }

        int slot = ctx.AllocSlot();
        ctx.RingDepth = slot + 1;
        var concat = Call(null, StringConcatObjectArrayInfo, NewArrayInit(typeof(object), parts));
        var handle = Call(ctx.HeapLocal, HeapAllocate, Convert(concat, typeof(object)));
        seq.Add(Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));
        return Block(seq);
    }

    /// <summary>
    /// Boxes a hole's ring value as a CLR object for <c>string.Concat</c>. The
    /// representation metadata decides: scalars and bools are converted by their CLR
    /// type (double from IEEE bits, bool from 0/1, ints via their own widening), and
    /// references / non-long value types are read from the heap. A null hole reads
    /// the reserved handle 0 as null, which Concat renders as the empty string.
    /// </summary>
    private static Expression HoleToClrObject(Node hole, Expression ringVal, Context ctx) {
        var (kind, clrType) = ctx.RepresentationOf(hole);
        if (clrType is null) {
            if (kind is ValueRepresentationKind.StackScalar)
                clrType = typeof(long);
            else if (kind is ValueRepresentationKind.HeapRef)
                return Convert(
                    Call(ctx.HeapLocal, HeapUnsafeGet, Convert(ringVal, typeof(int))), typeof(object));
            else
                throw new InvalidOperationException(
                    $"VM compile rejected: interpolation hole '{hole}' has no value representation.");
        }
        return Convert(RingCodec.ToClr(ringVal, clrType, hole, ctx.Analysis, ctx.HeapLocal), typeof(object));
    }

    private static Expression EmitInvokeIndirect(Invoke invoke, Context ctx) {
        if (ctx.Analysis?.GetMetadata<StoredLambdaMetadata>(invoke.Delegate) is { } stored
            && invoke.Arguments.Length != stored.Lambda.Parameters.Count)
            throw new InvalidOperationException(
                $"VM compile rejected: lambda has {stored.Lambda.Parameters.Count} parameter(s) but invoke has {invoke.Arguments.Length} argument(s).");
        var ring = ctx.OpenRing();
        int targetSlot = ring.Add(CompileNode(invoke.Delegate, ctx));
        int[] argSlots = new int[invoke.Arguments.Length];
        for (int i = 0; i < invoke.Arguments.Length; i++)
            argSlots[i] = ring.Add(CompileNode(invoke.Arguments[i], ctx));
        var argInits = new Expression[invoke.Arguments.Length];
        for (int i = 0; i < invoke.Arguments.Length; i++)
            argInits[i] = ctx.RingVar(argSlots[i]);
        int outSlot = ctx.AllocSlot();
        ctx.RingDepth = outSlot + 1;
        var call = Assign(ctx.RingVar(outSlot),
            Call(null, InvokeHeapClosureInfo,
                ctx.State,
                ctx.RingVar(targetSlot),
                ctx.FunctionTableExpr!,
                NewArrayInit(typeof(long), argInits)));
        return ring.Then(call);
    }

    private static long InvokeHeapClosure(VmState state, long handle, Action<VmState>[] table, long[] args) {
        if (handle <= 0)
            throw new InvalidOperationException("VM: invoke of null.");
        var obj = state.Heap.UnsafeGet((int)handle);
        if (obj is not object[] closure || closure.Length < 1 || closure[0] is not long idxL)
            throw new InvalidOperationException("VM: invoke target is not a closure.");
        for (int u = 1; u < closure.Length; u++) {
            if (closure[u] is not long[] { Length: >= 1 })
                throw new InvalidOperationException("VM: closure upvalue is not a cell.");
        }
        int idx = (int)idxL;
        if ((uint)idx >= (uint)table.Length || table[idx] is null)
            throw new InvalidOperationException("VM: invalid closure index.");

        return VmCall.Invoke(state, table[idx], args, (int)handle);
    }

    /// <summary>Inline a small lambda with no captures and a single-expression body.
    /// Skips frame push, closure allocation, and ring save/restore — compiles
    /// arguments directly to the ring and maps parameter reads to argument slots.</summary>
    private static Expression EmitInlineInvoke(Lambda lambda, Invoke invoke, Context ctx) {
        // Compile every argument before mapping any parameter: an argument
        // expression belongs to the caller's scope, so it must not see this
        // lambda's parameter names.
        var ring = ctx.OpenRing();
        int[] argSlots = new int[invoke.Arguments.Length];
        for (int i = 0; i < invoke.Arguments.Length; i++)
            argSlots[i] = ring.Add(CompileNode(invoke.Arguments[i], ctx));
        ctx.PushInlineParameters();
        for (int i = 0; i < invoke.Arguments.Length; i++)
            ctx.MapInlineParameter(lambda.Parameters[i], argSlots[i]);
        var bodyExpr = CompileNode(lambda.Body, ctx);
        ctx.PopInlineParameters();
        return ring.Then(bodyExpr);
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
    /// Captured bindings are read from/written to upvalue cells in
    /// <c>heap[state.ClosureHandle][captureIndex + 1]</c> (<c>long[1]</c>).
    /// </summary>
    private static Action<VmState> CompileFunctionBody(
        Node body,
        IReadOnlyList<Parameter> parameters,
        List<Capture> captures,
        Action<VmState>[] functionTable,
        CompilationMode mode,
        AnalysisResult analysis,
        HashSet<object> capturedBindings,
        bool instanceSlot = false,
        Type? asyncReturn = null) {

        var fnCtx = new Context();
        fnCtx.FunctionTableExpr = Constant(functionTable);
        fnCtx.Mode = mode;
        fnCtx.Analysis = analysis;
        fnCtx.AsyncReturn = asyncReturn;
        fnCtx.IsCompiledFunctionBody = true;
        fnCtx.CapturedBindings = capturedBindings;
        // Instance calls pass the receiver handle at frame slot 0. User
        // parameters start at slot 1 and are matched by name.
        if (instanceSlot)
            fnCtx.ParamSlotOffset = -1;
        var bodyExprs = new List<Expression>();

        bodyExprs.Add(Label(fnCtx.EntryLabel));
        bodyExprs.Add(Assign(fnCtx.SlotsLocal, fnCtx.SlotsInitExpression));
        bodyExprs.Add(Assign(fnCtx.HeapLocal, fnCtx.HeapInitExpression));
        bodyExprs.Add(Assign(fnCtx.FramePosLocal,
            Property(fnCtx.State, nameof(VmState.FramePos))));
        if (instanceSlot) {
            bodyExprs.Add(Assign(fnCtx.InstanceHandle,
                ArrayAccess(fnCtx.SlotsLocal, fnCtx.FramePosLocal)));
        }

        if (mode != CompilationMode.NoDebug) {
            fnCtx.DebugHookProp = Property(fnCtx.State, nameof(VmState.DebugHook));
        }

        // Register captures so EmitVariable/EmitAssignment can route to heap reads
        for (int i = 0; i < captures.Count; i++)
            fnCtx.DeclareCapture(captures[i].Binding, i);

        // Declare parameters as value-stack variables (mapped to _slots[_fp + idx]).
        // Locals start after those slots. An instance frame also keeps slot 0
        // for the receiver. Without the reserve, block-local zeros land on the
        // arguments (CreatePermits plate became 0 before Permit.Create).
        fnCtx.PushScope();
        fnCtx.PushFrameParameters();
        foreach (var param in parameters)
            fnCtx.MapFrameParameter(param, fnCtx.DeclareParameter(param));
        fnCtx.ReserveFrameSlots(parameters.Count + (instanceSlot ? 1 : 0));

        // Compile body
        var bodyCompiled = CompileNode(body, fnCtx);
        bodyExprs.Add(bodyCompiled);

        // Flush result: return value at _slots[_fp], SP = _fp + 1.
        // An IsAsync method returns a completed Task / Task<T> instead of the body's value.
        Expression? resultValue = fnCtx.RingDepth > 0 ? fnCtx.RingVar(fnCtx.RingDepth - 1) : null;
        if (asyncReturn is not null)
            resultValue = WrapAsyncResult(resultValue, asyncReturn, fnCtx, YieldingNode(body));
        if (resultValue is not null) {
            bodyExprs.Add(Assign(ArrayAccess(fnCtx.SlotsLocal, fnCtx.FramePosLocal), resultValue));
            bodyExprs.Add(Assign(fnCtx.SlotsStackPointer,
                Add(fnCtx.FramePosLocal, Constant(1))));
        }

        bodyExprs.Add(Label(fnCtx.ExitLabel));

        var delegateExpr = Lambda<Action<VmState>>(Block(fnCtx.Locals, bodyExprs), fnCtx.State);
        return delegateExpr.Compile();
    }

    /// <summary>
    /// The declared CLR return type of an <c>IsAsync</c> AST method when it is one the VM wraps:
    /// <see cref="Task"/> or <c>Task&lt;T&gt;</c>. Any other return type is left unwrapped, and a
    /// method that is not <c>IsAsync</c> is never wrapped.
    /// </summary>
    private static Type? AsyncReturnType(AstMethodDefinition method) {
        if (!method.DefinitionNode.IsAsync)
            return null;
        var declared = method.MemberTypeDefinition.GetRuntimeType();
        if (declared == typeof(Task))
            return declared;
        return declared is { IsGenericType: true }
            && declared.GetGenericTypeDefinition() == typeof(Task<>)
            ? declared
            : null;
    }

    /// <summary>True when <paramref name="node"/> or any descendant is an <see cref="Await"/>.</summary>
    private static bool ContainsAwait(Node node) {
        if (node is Await)
            return true;
        foreach (var child in node.Children)
            if (child is not null && ContainsAwait(child))
                return true;
        return false;
    }

    private static readonly MethodInfo TaskFromResultInfo =
        typeof(Task).GetMethod(nameof(Task.FromResult))!;
    private static readonly PropertyInfo TaskCompletedTaskInfo =
        typeof(Task).GetProperty(nameof(Task.CompletedTask))!;

    /// <summary>
    /// The node whose value is a function body's result, when it is a single expression:
    /// an explicit <c>return v</c>, the last statement of a block, or the body itself when it
    /// is an expression body. A branching body has no single yielding node.
    /// </summary>
    private static Node? YieldingNode(Node body) => body switch {
        Return { Value: { } value } => value,
        Block { Nodes.Count: > 0 } block => YieldingNode(block.Nodes[^1]),
        IfStatement => null,
        _ => body,
    };

    private static bool IsTaskLike(Type? type) =>
        type == typeof(Task)
        || (type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(Task<>));

    /// <summary>
    /// Wrap the body's yielded value into the completed task an <c>IsAsync</c> method returns:
    /// <c>Task.FromResult&lt;T&gt;(v)</c> for <c>Task&lt;T&gt;</c>, or <c>Task.CompletedTask</c>
    /// for <c>Task</c>. The task is heap-allocated like any other reference the ABI returns.
    /// <paramref name="source"/> is the yielding node, for the ring's double/float un-bitting and
    /// for rejecting a body that yields a task where the element <c>T</c> is expected.
    /// </summary>
    private static Expression WrapAsyncResult(Expression? bodyValue, Type asyncReturn, Context ctx, Node? source) {
        if (asyncReturn == typeof(Task))
            return RingCodec.FromClr(Property(null, TaskCompletedTaskInfo), asyncReturn, ctx.HeapLocal);
        var resultType = asyncReturn.GetGenericArguments()[0];
        if (bodyValue is null)
            throw new InvalidOperationException(
                $"VM compile rejected: async method returning '{asyncReturn}' has no body result.");
        var yieldedClr = source is null ? null : ctx.Analysis?.GetResolvedType(source)?.GetRuntimeType();
        if (!IsTaskLike(resultType) && IsTaskLike(yieldedClr))
            throw new InvalidOperationException(
                $"VM compile rejected: async method returning '{asyncReturn}' yields '{yieldedClr}' — return the value, not the task.");
        var yielded = RingCodec.ToClr(bodyValue, resultType, source, ctx.Analysis, ctx.HeapLocal);
        var task = Call(TaskFromResultInfo.MakeGenericMethod(resultType), yielded);
        return RingCodec.FromClr(task, asyncReturn, ctx.HeapLocal);
    }

    // Compiled AST constructors and methods, per definition node and compilation
    // mode. A slot is published here only after its body compiled.
    private static readonly object AstCallableGate = new();
    private static readonly ConditionalWeakTable<Node, Dictionary<CompilationMode, Action<VmState>[]>> AstCallables = new();

    // Slots this thread is compiling, outermost first. A recursive call finds its own
    // slot here; everything compiled under an outermost call is published together
    // when that call succeeds.
    [ThreadStatic] private static List<(Node Key, CompilationMode Mode, Action<VmState>[] Slot)>? _astPending;

    private static readonly MethodInfo InvokeAstFunctionInfo =
        typeof(AbiEmitter).GetMethod(nameof(InvokeAstFunction),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Call a compiled AST constructor or method. <paramref name="args"/>[0] is
    /// the receiver handle when the member is an instance member. Return writes
    /// <c>slots[FramePos]</c> and jumps to this function's exit, not the program exit.
    /// </summary>
    private static long InvokeAstFunction(VmState state, Action<VmState> function, long[] args) {
        if (function is null)
            throw new InvalidOperationException("VM: AST member is not compiled.");
        return VmCall.Invoke(state, function, args);
    }

    private static bool HasExecutableBody(Node? body) =>
        body is not null && body is not Block { Nodes.Count: 0 };

    /// <summary>
    /// Compiles the arguments of an AST constructor or method call (appended to
    /// <paramref name="seq"/>) and returns one callee frame value per parameter.
    /// A missing argument takes the parameter default. A stack scalar passed to a
    /// reference parameter is heap-allocated, because the callee reads that slot
    /// as a heap handle.
    /// </summary>
    private static List<Expression> EmitAstArguments(
        IReadOnlyList<Node> arguments, IReadOnlyList<Parameter> parameters,
        string callee, List<Expression> seq, Context ctx) {
        if (arguments.Count > parameters.Count)
            throw new InvalidOperationException(
                $"VM compile rejected: {callee} has {parameters.Count} parameter(s) but the call has {arguments.Count} argument(s).");

        var values = new List<Expression>(parameters.Count);
        var ring = ctx.OpenRing();
        for (int i = 0; i < arguments.Count; i++) {
            int got = ring.Add(CompileNode(arguments[i], ctx));
            values.Add(RingCodec.IsStackScalar(arguments[i], ctx.Analysis) && !ParameterIsValueType(parameters[i])
                ? Convert(
                    Call(ctx.HeapLocal, HeapAllocate, Convert(ctx.RingVar(got), typeof(object))),
                    typeof(long))
                : ctx.RingVar(got));
        }
        for (int i = arguments.Count; i < parameters.Count; i++)
            values.Add(ParameterDefaultLong(parameters[i], ctx));
        seq.AddRange(ring.Steps);
        return values;
    }

    private static Expression EmitAstInvoke(
        Invoke invoke, Member member, AstMethodDefinition method, Action<VmState>[] table, Context ctx) {
        var definition = method.DefinitionNode;
        bool isStatic = method.IsStatic;
        var seq = new List<Expression>();
        Expression? receiver = null;
        if (!isStatic) {
            if (IsTypeNameReceiver(member.Value, ctx))
                throw new InvalidOperationException(
                    $"Instance method '{member.MemberName}' cannot be invoked on a type name.");
            var receiverRing = ctx.OpenRing();
            int instanceSlot = receiverRing.Add(CompileNode(member.Value, ctx));
            seq.AddRange(receiverRing.Steps);
            receiver = ctx.RingVar(instanceSlot);
        }

        var parameters = definition.Parameters ?? [];
        var args = EmitAstArguments(invoke.Arguments, parameters, $"method '{method.Name}'", seq, ctx);
        var inits = new List<Expression>(parameters.Count + 1);
        if (receiver is not null)
            inits.Add(receiver);
        inits.AddRange(args);

        int result = ctx.AllocSlot();
        ctx.RingDepth = result + 1;
        var call = Call(
            null, InvokeAstFunctionInfo, ctx.State,
            ArrayIndex(Constant(table), Constant(0)),
            NewArrayInit(typeof(long), inits));
        var returnClr = method.MemberTypeDefinition.GetRuntimeType();
        bool isVoid = returnClr == typeof(void)
            || string.Equals(method.MemberTypeDefinition.Name, "void", StringComparison.Ordinal);
        if (isVoid) {
            seq.Add(call);
            seq.Add(Assign(ctx.RingVar(result), Constant(0L)));
        }
        else {
            seq.Add(Assign(ctx.RingVar(result), call));
        }
        return Block(seq);
    }

    private static Action<VmState>[]? TryCompileAstCallable(
        Node key,
        Node body,
        IReadOnlyList<Parameter> parameters,
        ITypeDefinition declaring,
        Context caller,
        bool instanceSlot,
        Type? asyncReturn = null) {
        var mode = caller.Mode;
        lock (AstCallableGate) {
            if (AstCallables.TryGetValue(key, out var byMode) && byMode.TryGetValue(mode, out var done))
                return done;
        }
        var pending = _astPending ??= [];
        foreach (var entry in pending) {
            if (ReferenceEquals(entry.Key, key) && entry.Mode == mode)
                return entry.Slot;
        }

        var start = pending.Count;
        var slot = new Action<VmState>[1];
        pending.Add((key, mode, slot));
        try {
            var lambdas = new List<Lambda>();
            CollectLambdas(body, lambdas);
            if (lambdas.Count > 0)
                throw new InvalidOperationException(
                    "VM compile rejected: a lambda inside an AST constructor or method is not yet compiled as its own frame.");
            var analysis = AnalysisForAstBody(body, declaring, caller.Analysis, key);
            if (analysis.HasErrors) {
                var text = string.Join("; ", analysis.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Take(8)
                    .Select(d => d.Message));
                throw new InvalidOperationException(
                    "VM compile rejected: AST member body analysis reported errors: " + text);
            }
            slot[0] = CompileFunctionBody(
                body,
                parameters,
                [],
                [],
                mode,
                analysis,
                new HashSet<object>(ReferenceEqualityComparer.Instance),
                instanceSlot,
                asyncReturn);
        }
        catch (Exception ex) {
            // Drop this slot and everything compiled under it: those bodies may
            // call this slot, which stays empty.
            pending.RemoveRange(start, pending.Count - start);
            if (ex is not (InvalidOperationException or NotSupportedException))
                throw;
            // An IsAsync method's body must compile: falling back to a CLR lookup of the same
            // name would run a different method. Surface the body's compile reject instead.
            if (asyncReturn is not null)
                throw;
            // A body that only forwards to a CLR static method (host jobs are built
            // this way) must not fall through to a CLR method of the same name.
            if (key is MethodDefinitionNode forwarder && ForwardsToClrStatic(body))
                throw new InvalidOperationException(
                    $"VM compile rejected: AST method '{forwarder.Name}' forwards to a CLR static method and could not be compiled. {ex.Message}",
                    ex);
            return null;
        }

        if (start == 0) {
            lock (AstCallableGate) {
                foreach (var (k, m, s) in pending)
                    AstCallables.GetOrCreateValue(k)[m] = s;
            }
            pending.Clear();
        }
        return slot;
    }

    /// <summary>
    /// True when <paramref name="body"/> is one call (or return of a call) on a CLR
    /// type's static member, as in <c>{ return Ops.Job(this, a); }</c>.
    /// </summary>
    private static bool ForwardsToClrStatic(Node body) {
        var statement = body is Block { Nodes.Count: 1 } block ? block.Nodes[0] : body;
        if (statement is Return { Value: { } value })
            statement = value;
        return statement is Invoke { Delegate: Member { Value: ClrTypeReference } };
    }

    private static bool ParameterIsValueType(Parameter parameter) {
        if (parameter.TypeReference is PrimitiveTypeReference prim)
            return prim.PrimitiveId.GetClrType() is { IsValueType: true };
        if (parameter.TypeReference is ClrTypeReference clr)
            return clr.RuntimeType.IsValueType;
        return false;
    }

    private static AnalysisResult AnalysisForAstBody(
        Node body, ITypeDefinition declaring, AnalysisResult? caller, Node member) {
        if (caller is not null && AstBodyMembersResolved(body, caller))
            return caller;
        if (declaring is not AstTypeDefinition astType)
            throw new InvalidOperationException(
                "VM compile rejected: AST member body is not on an AST type.");
        // Copy the provider. Analyzer reuses a TypeDefinitionProviderCollection
        // and the type-definition pass pushes a registry onto it.
        var provider = astType.ResolutionProvider;
        if (provider is TypeDefinitionProviderCollection collection)
            provider = new TypeDefinitionProviderCollection(collection.Providers.ToArray());
        // Re-analyze the declaring type so members resolve. A typed Create
        // forwarder shares that type with printed helpers the VM analyzer
        // still flags (C8-vm). Diagnostics outside this method must not reject
        // the forwarder. An error on the method, its signature, or its body
        // still fails it. Every other member keeps the type-level result, so a
        // dirty type still falls through to InvokeNamed.
        var analysis = Interpreter.Analyzer.Analyze(
            astType.Syntax,
            typeDefinitions: provider,
            setup: ctx => ctx.SetMetadata(astType.Syntax, new TypeDefinitionMetadata(astType)));
        if (!analysis.HasErrors
            || !ForwardsToClrStatic(body)
            || ErrorsTouch(member, analysis))
            return analysis;
        return analysis with {
            HasErrors = false,
            Diagnostics = analysis.Diagnostics
                .Where(d => d.Severity != DiagnosticSeverity.Error)
                .ToArray()
        };
    }

    private static bool ErrorsTouch(Node body, AnalysisResult analysis) {
        foreach (var diagnostic in analysis.Diagnostics) {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
                continue;
            if (ReferenceEquals(diagnostic.Node, body) || ContainsNode(body, diagnostic.Node))
                return true;
        }
        return false;
    }

    private static bool ContainsNode(Node root, Node target) {
        foreach (var child in root.Children) {
            if (child is null)
                continue;
            if (ReferenceEquals(child, target) || ContainsNode(child, target))
                return true;
        }
        return false;
    }

    private static bool AstBodyMembersResolved(Node node, AnalysisResult analysis) {
        // Reusing the caller's analysis is only safe when it actually analyzed this
        // body: a Variable use must carry the bind bag (VariableReferences /
        // ParameterReferences), or the emitter cannot project its declaration type.
        if (node is Variable variable
            && analysis.GetMetadata<VariableAnalysisMetadata>(variable) is null)
            return false;
        if (node is Member member && analysis.GetResolvedMember(member) is null)
            return false;
        foreach (var child in node.Children) {
            if (child is not null && !AstBodyMembersResolved(child, analysis))
                return false;
        }
        return true;
    }

    private static Expression ParameterDefaultLong(Parameter parameter, Context ctx) {
        if (parameter.DefaultValue is not Constant constant)
            return Constant(0L);
        if (RingCodec.TryInlineConstant(constant.Value, out long scalar))
            return Constant(scalar);
        return Convert(
            Call(ctx.HeapLocal, HeapAllocate, Convert(Constant(constant.Value), typeof(object))),
            typeof(long));
    }

    // ── Helpers ────────────────────────────────────

    /// <summary>
    /// Wrap a compiled node expression with a simplified debug hook guard.
    /// In NoDebug mode (<see cref="Context.DebugHookProp"/> is null),
    /// returns the body unchanged — zero overhead.
    ///
    /// When a hook is set, emits code that snapshots the current frame's
    /// locals into a <c>long[]</c> buffer (using compile-time known offsets),
    /// then invokes <c>DebugHook(node, ReadOnlySpan&lt;long&gt;(buffer), heap)</c>.
    ///
    /// The <c>ProgramCounter</c> is also flushed to the current step for
    /// legacy compatibility.
    /// </summary>
    private static Expression WithInterrupt(Expression body, Context ctx, int step) {
        if (ctx.DebugHookProp is null) return body;

        int localCount = ctx.FrameSlotHighWater;

        // Build the hook invocation expression only when the hook is non-null.
        // Emit: if (state.DebugHook != null) { snapshot + invoke }
        var stmts = new List<Expression>();
        var flush = ctx.EmitScopeStores();
        if (flush.Count > 0)
            stmts.Add(Block(flush));

        // Create a ReadOnlySpan<long> directly over _slots[_fb .. _fb + localCount].
        // This is a zero-allocation slice — no buffer copy needed.
        // Cell-backed slots hold heap handles; VmDebugger.GetLocals derefs cell[0].
        Expression spanExpr = localCount == 0
            ? New(ReadOnlySpanLongArrayCtor,
                NewArrayBounds(typeof(long), Constant(0)))
            : New(ReadOnlySpanLongSliceCtor,
                ctx.SlotsLocal, ctx.FramePosLocal, Constant(localCount));

        stmts.Add(Invoke(ctx.DebugHookProp, ctx.CurrentAstNodeExpr, spanExpr, ctx.HeapLocal));

        return Block(
            IfThen(
                NotEqual(ctx.DebugHookProp, Constant(null, typeof(Action<Node, ReadOnlySpan<long>, VmManagedObjectHeap>))),
                Block(Assign(ctx.StatePcFlush, Constant(step)), Block(stmts))),
            body);
    }

    // ── Static property/method refs ─────────────────────────────────

    private static readonly PropertyInfo StateStackProperty =
        Ref<VmState>.Property(e => e.Stack);
    private static readonly PropertyInfo ValueStackRawSlotsProperty =
        Ref<VmValueStack>.Property(s => s.RawSlots);
    private static readonly PropertyInfo ValueStackStackPointerProperty =
        Ref<VmValueStack>.Property(s => s.StackPointer);
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
    private static readonly MethodInfo InvokeHeapClosureInfo =
        typeof(AbiEmitter).GetMethod(nameof(InvokeHeapClosure),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

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

    private static System.Collections.IEnumerator GetEnumeratorOrThrow(object? collection) {
        if (collection is null)
            throw new InvalidOperationException("foreach collection is null.");
        if (collection is System.Collections.IEnumerable enumerable)
            return enumerable.GetEnumerator();
        throw new InvalidOperationException(
            $"foreach collection type '{collection.GetType().Name}' is not IEnumerable.");
    }

    /// <summary>
    /// Instance call on a heap value whose handle is null. Names the member
    /// (<c>'Expiry' is not set</c>) instead of a raw null dereference.
    /// Scalars stay on the ring; only heap receivers are checked.
    /// </summary>
    private static Expression? UnsetReceiverGuard(Node receiver, Expression handle, Context ctx) {
        if (receiver is not Member member || IsTypeNameReceiver(member.Value, ctx))
            return null;
        if (ctx.KindOf(receiver) is not ValueRepresentationKind.HeapRef)
            return null;
        var ctor = typeof(Poly.DomainModeling.Runtime.ConstraintFailureException)
            .GetConstructor([typeof(string)]);
        if (ctor is null)
            return null;
        return IfThen(
            Equal(handle, Constant(0L)),
            Throw(New(ctor, Constant($"'{member.MemberName}' is not set"))));
    }

    private static Expression MissingClrArgument(ParameterInfo parameter) {
        if (!parameter.HasDefaultValue)
            throw new InvalidOperationException(
                $"VM compile rejected: argument '{parameter.Name}' of '{parameter.Member.Name}' has no value.");
        var value = parameter.DefaultValue;
        if (value is null
            && parameter.ParameterType.IsValueType
            && Nullable.GetUnderlyingType(parameter.ParameterType) is null)
            return Default(parameter.ParameterType);
        return Constant(value, parameter.ParameterType);
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