using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

public static partial class AbiEmitter {

    /// <summary>
    /// A call's ring outcome, the one convention every invocation path uses: a value is
    /// converted by its CLR type — an inline word, an IEEE bit pattern, or a heap handle —
    /// and a <c>void</c> call leaves <c>0L</c>, so the slot the caller reserved is always
    /// written. <paramref name="returnType"/> is null when analysis could not resolve it;
    /// the runtime codec then classifies the boxed value.
    /// </summary>
    private static Expression EmitResult(Expression call, Type? returnType, Context ctx) =>
        returnType == typeof(void)
            ? Block(call, Constant(0L))
            : RingCodec.ToFrame(call, returnType ?? call.Type, ctx.HeapLocal);

    /// <summary>
    /// Invoke: for Lambda targets, compile the body inline (no separate delegate).
    /// </summary>
    private static Expression EmitInvoke(Invoke invoke, Context ctx) {
        // Handle Invoke(Member(instance, "Method"), args) — resolve the method
        // and call it directly via CLR reflection, bypassing full lambda handling.
        if (invoke.Delegate is Member member) {
            // Set when an AST method body fails to compile and the call falls through to
            // name dispatch, so the runtime failure can name the compile reject.
            string? astBodyReject = null;
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

                    var methodParams = methodInfo.GetParameters();

                    // Compile arguments — track actual result slots. A Lambda passed
                    // to a delegate/Expression<D> parameter is not a VM closure value:
                    // it becomes a nested CLR lambda and is emitted straight into the
                    // call argument (see DirectVmAbiEmitter.LambdaBridge.cs). A bridged
                    // argument contributes a prelude step but no ring slot.
                    var args = ctx.OpenRing();
                    int[] argSlots = new int[invoke.Arguments.Length];
                    var bridgeArgs = new Expression?[invoke.Arguments.Length];
                    for (int i = 0; i < invoke.Arguments.Length; i++) {
                        var parameterType = i < methodParams.Length
                            ? methodParams[i].ParameterType
                            : null;
                        if (TryBuildLambdaBridgeArgument(
                                invoke.Arguments[i], parameterType, ctx,
                                out var bridgePrelude, out var bridgeArgument)) {
                            args.AddUncounted(bridgePrelude);
                            bridgeArgs[i] = bridgeArgument;
                            continue;
                        }
                        argSlots[i] = args.Add(CompileNode(invoke.Arguments[i], ctx));
                    }
                    int slot = args.Base + args.Count;
                    ctx.RingDepth = slot + 1;

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
                            if (bridgeArgs[i] is { } bridgeArgument) {
                                methodArgs[i] = bridgeArgument;
                                continue;
                            }
                            var ringVal = ctx.RingVar(argSlots[i]);
                            var parameterType = methodParams[i].ParameterType;
                            var sourceClr = ctx.ClrTypeOf(invoke.Arguments[i]);
                            methodArgs[i] = Nullable.GetUnderlyingType(parameterType) is not null
                                && sourceClr is not null
                                && Nullable.GetUnderlyingType(sourceClr) is not null
                                ? new SlotLocation.Ring(argSlots[i]).ReadAs(ctx, parameterType)
                                : RingCodec.ToClr(
                                    ringVal, parameterType, invoke.Arguments[i],
                                    ctx.Analysis, ctx.HeapLocal);
                        }
                    }

                    Expression callExpr;
                    if (isStatic) {
                        callExpr = Call(null, methodInfo, methodArgs);
                    }
                    else {
                        var declaringType = methodInfo.DeclaringType!;
                        var receiverClr = ctx.ClrTypeOf(member.Value) ?? declaringType;
                        callExpr = Call(
                            InstanceCallTarget(ctx.RingVar(instanceSlot), receiverClr, declaringType, ctx),
                            methodInfo, methodArgs);
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

                    fullBody.Add(Assign(ctx.RingVar(slot),
                        EmitResult(callExpr, methodInfo.ReturnType, ctx)));
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
                        ctx,
                        instanceSlot: !astMethod.IsStatic,
                        rejection: out var rejection,
                        asyncReturn: asyncReturn);
                    if (table is not null)
                        return EmitAstInvoke(invoke, member, astMethod, table, ctx);
                    // The body exists and resolved to this AST method, so a name-dispatched
                    // fallback can only be right on an instance that is not ours to shape.
                    // `new T()` is the VM's own dictionary, which has no such method: emit
                    // nothing and reject here, while the reason is still in hand, rather
                    // than fail at runtime naming the storage type.
                    if (IsVmConstructedInstance(member.Value, ctx))
                        throw new InvalidOperationException(
                            $"VM compile rejected: AST method '{astMethod.Name}' has no compilable body and "
                            + "its receiver is an instance the VM constructs, so there is no host method to "
                            + $"dispatch to. {Reason(rejection)}");
                    astBodyReject = rejection;
                }
            }

            // ITypeMethod without MethodInfo, or Member that analysis did not
            // resolve as a method: live instance by name+arity, then
            // InvokeNamed(string, object?[]). Fail-closed inside InvokeInstanceMethod.
            // `astBodyReject` is set when we get here because an AST method body failed,
            // so the failure can name that reason instead of only the receiver's type.
            if (IsTypeNameReceiver(member.Value, ctx))
                throw new InvalidOperationException(
                    $"Member '{member.MemberName}' on a type name is not a resolved static method.");

            var astReceiver = ctx.OpenRing();
            var astInstanceSlot = astReceiver.AddValue(CompileNode(member.Value, ctx));

            var astArgs = ctx.OpenRing();
            var astArgSlots = new Value[invoke.Arguments.Length];
            for (int i = 0; i < invoke.Arguments.Length; i++)
                astArgSlots[i] = astArgs.AddValue(CompileNode(invoke.Arguments[i], ctx));

            var astInstanceObj = InstanceObject(astInstanceSlot.Read, ctx.ClrTypeOf(member.Value), ctx);
            var argObjs = new Expression[invoke.Arguments.Length];
            for (int i = 0; i < invoke.Arguments.Length; i++) {
                // An argument the emitter cannot place is a compile error, not a handle.
                bool scalar = RingCodec.IsStackScalar(invoke.Arguments[i], ctx.Analysis);
                argObjs[i] = scalar
                    ? ScalarArgument(astArgSlots[i].Read, ctx.ClrTypeOf(invoke.Arguments[i]), ctx)
                    : Call(ctx.HeapLocal, HeapUnsafeGet,
                        Convert(astArgSlots[i].Read, typeof(int)));
            }

            var invokeCall = Call(
                null,
                InvokeInstanceMethodInfo,
                astInstanceObj,
                Constant(member.MemberName),
                NewArrayInit(typeof(object), argObjs),
                Constant(astBodyReject, typeof(string)));

            var astSlot = ctx.Reserve();
            ctx.RingDepth = astSlot.RingSlot + 1;
            var astBody = new List<Expression>(astArgs.Count + 4);
            astBody.AddRange(astReceiver.Steps);
            astBody.AddRange(astArgs.Steps);
            var unsetReceiver = UnsetReceiverGuard(member.Value, astInstanceSlot.Read, ctx);
            if (unsetReceiver is not null)
                astBody.Add(unsetReceiver);

            // The declared return type is the only one a name-dispatched call has; an
            // unresolved member leaves the value to the runtime codec.
            var returnClr = method?.MemberTypeDefinition.GetRuntimeType();
            bool isVoid = method is not null
                && (returnClr == typeof(void)
                    || string.Equals(method.MemberTypeDefinition.Name, "void", StringComparison.Ordinal));
            astBody.Add(Assign(astSlot.Read,
                EmitResult(invokeCall, isVoid ? typeof(void) : returnClr, ctx)));
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
            foreach (var param in lambda.Parameters)
                ctx.DeclareParameter(param);
            int saveDepth = ctx.RingDepth;

            var preBody = new List<Expression>();   // before inline body
            var postBody = new List<Expression>();  // after inline body
            var spProp = Property(Property(ctx.State, "Stack"), "StackPointer");

            using var callSpLease = ctx.LeaseHelper(typeof(int), "_callSp");
            using var callerFramePosLease = ctx.LeaseHelper(typeof(int), "_callerFp");
            var callSp = callSpLease.Local;
            var callerFramePos = callerFramePosLease.Local;
            preBody.Add(Assign(callSp, spProp));
            // SetArgs seeded the arguments at slots[callSp + index]; the frame pointer
            // is that first slot, so parameters resolve at FP + index. The caller's
            // frame pointer is restored below, as VmCall.Invoke restores it after a call.
            preBody.Add(Assign(callerFramePos, ctx.FramePosLocal));
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
            // No copy-out is needed: the body's block allocates from saveDepth upward
            // and every emitter rewinds only to its own region base (the lowest is
            // RingDepth = ring.Base inside an index access), so the caller's slots below
            // saveDepth are never targets. The result belongs in the caller's first free
            // slot, and moves only when it did not already land there.
            ctx.RingDepth = saveDepth + 1;
            // The caller's slot below `saveDepth` is a word, so a typed body result is
            // converted once here rather than left as a typed local nobody reports.
            if (bodyResultSlot != saveDepth
                || RingCodec.IsTypedSlot(RingCodec.SlotTypeOf(bodyExpr.ClrType)))
                postBody.Add(Assign(ctx.RingVar(saveDepth),
                    ctx.RingWord(bodyResultSlot, bodyExpr.ClrType)));
            postBody.Add(Assign(ctx.FramePosLocal, callerFramePos));
            // The block must end on its value: its own type is what the caller reports as
            // the result's representation, and the frame restore above is an int.
            postBody.Add(ctx.RingVar(saveDepth));

            ctx.ParamSlotOffset = savedParamOffset;  // restore
            ctx.RestoreParamSlots(savedNextParamSlot);  // restore outer param slot counter
            ctx.PopScope();
            return Block(preBody.Concat(postBody));
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
            var holeSlot = holeRing.AddValue(CompileNode(hole, ctx));
            parts.Add(HoleToClrObject(hole, holeSlot.Read, ctx));
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
        if (ctx.Analysis is { } analysis
            && StoredLambdaBinding.TryGet(analysis, invoke.Delegate, out var stored)
            && stored is not null
            && invoke.Arguments.Length != stored.Lambda.Parameters.Count)
            throw new InvalidOperationException(
                $"VM compile rejected: lambda has {stored.Lambda.Parameters.Count} parameter(s) but invoke has {invoke.Arguments.Length} argument(s).");
        var ring = ctx.OpenRing();
        var targetSlot = ring.AddValue(CompileNode(invoke.Delegate, ctx));
        var argSlots = new Value[invoke.Arguments.Length];
        for (int i = 0; i < invoke.Arguments.Length; i++)
            argSlots[i] = ring.AddValue(CompileNode(invoke.Arguments[i], ctx));
        var argInits = new Expression[invoke.Arguments.Length];
        for (int i = 0; i < invoke.Arguments.Length; i++)
            argInits[i] = argSlots[i].Read;
        var outSlot = ctx.Reserve();
        ctx.RingDepth = outSlot.RingSlot + 1;
        var call = Assign(outSlot.Read,
            Call(null, InvokeHeapClosureInfo,
                ctx.State,
                targetSlot.Read,
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

    /// <summary>A lambda an <see cref="Invoke"/> names as its delegate is emitted in the
    /// caller's frame, never materialized as a closure, so it needs no function-table entry.
    /// </summary>
    private static void CollectInlineInvokedLambdas(Node node, HashSet<Lambda> result) {
        if (node is Invoke { Delegate: Lambda inlined })
            result.Add(inlined);
        foreach (var child in node.Children) {
            if (child is not null)
                CollectInlineInvokedLambdas(child, result);
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
        // parameters start at slot 1 and are matched by the parameter node.
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
        foreach (var param in parameters)
            fnCtx.DeclareParameter(param);
        fnCtx.ReserveFrameSlots(parameters.Count + (instanceSlot ? 1 : 0));

        // Compile body
        var bodyCompiled = CompileNode(body, fnCtx);
        bodyExprs.Add(bodyCompiled);

        // Flush result: the callee always leaves its result word at _slots[_fp] — 0L for
        // a body that yields none — so a caller never reads a slot the callee left
        // untouched. An IsAsync method returns a completed Task / Task<T> instead.
        Expression? resultValue = fnCtx.RingDepth > 0
            ? fnCtx.RingWord(fnCtx.RingDepth - 1, bodyCompiled.ClrType)
            : null;
        if (asyncReturn is not null)
            resultValue = WrapAsyncResult(resultValue, asyncReturn, fnCtx, YieldingNode(body));
        bodyExprs.Add(Assign(ArrayAccess(fnCtx.SlotsLocal, fnCtx.FramePosLocal),
            resultValue ?? Constant(0L)));
        bodyExprs.Add(Assign(fnCtx.SlotsStackPointer,
            Add(fnCtx.FramePosLocal, Constant(1))));

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
            return RingCodec.ToFrame(Property(null, TaskCompletedTaskInfo), asyncReturn, ctx.HeapLocal);
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
        return RingCodec.ToFrame(task, asyncReturn, ctx.HeapLocal);
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

    /// <summary>The widest call frame passed word by word. A wider frame keeps the
    /// <c>long[]</c> form — the same protocol, only the plumbing differs.</summary>
    private const int MaxDirectFrameWords = 8;

    /// <summary>The <see cref="InvokeAstFrame"/> overload for each direct arity, indexed by
    /// word count minus one. Resolution is by parameter count, so a missing overload fails
    /// at type load rather than emitting a frame the runtime cannot fill.</summary>
    private static readonly MethodInfo[] InvokeAstFrameArities = ResolveInvokeAstFrameArities();

    private static MethodInfo[] ResolveInvokeAstFrameArities() {
        var overloads = typeof(AbiEmitter)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name == nameof(InvokeAstFrame))
            .ToArray();
        return Enumerable.Range(1, MaxDirectFrameWords)
            .Select(words => overloads.Single(
                method => method.GetParameters().Length == 2 + words))
            .ToArray();
    }

    /// <summary>
    /// <see cref="InvokeAstFunction"/> with the frame's words named one by one, so the
    /// call site allocates nothing. Same frame protocol: word <c>i</c> becomes
    /// <c>slots[FramePos + i]</c> either way.
    /// </summary>
    private static long InvokeAstFrame(VmState state, Action<VmState> function, long a0) {
        if (function is null)
            throw new InvalidOperationException("VM: AST member is not compiled.");
        return VmCall.Invoke(state, function, a0);
    }

    /// <inheritdoc cref="InvokeAstFrame(VmState, Action{VmState}, long)"/>
    private static long InvokeAstFrame(VmState state, Action<VmState> function, long a0, long a1) {
        if (function is null)
            throw new InvalidOperationException("VM: AST member is not compiled.");
        return VmCall.Invoke(state, function, a0, a1);
    }

    /// <inheritdoc cref="InvokeAstFrame(VmState, Action{VmState}, long)"/>
    private static long InvokeAstFrame(
        VmState state, Action<VmState> function, long a0, long a1, long a2) {
        if (function is null)
            throw new InvalidOperationException("VM: AST member is not compiled.");
        return VmCall.Invoke(state, function, a0, a1, a2);
    }

    /// <inheritdoc cref="InvokeAstFrame(VmState, Action{VmState}, long)"/>
    private static long InvokeAstFrame(
        VmState state, Action<VmState> function, long a0, long a1, long a2, long a3) {
        if (function is null)
            throw new InvalidOperationException("VM: AST member is not compiled.");
        return VmCall.Invoke(state, function, a0, a1, a2, a3);
    }

    /// <inheritdoc cref="InvokeAstFrame(VmState, Action{VmState}, long)"/>
    private static long InvokeAstFrame(
        VmState state, Action<VmState> function, long a0, long a1, long a2, long a3, long a4) {
        if (function is null)
            throw new InvalidOperationException("VM: AST member is not compiled.");
        return VmCall.Invoke(state, function, a0, a1, a2, a3, a4);
    }

    /// <inheritdoc cref="InvokeAstFrame(VmState, Action{VmState}, long)"/>
    private static long InvokeAstFrame(
        VmState state, Action<VmState> function,
        long a0, long a1, long a2, long a3, long a4, long a5) {
        if (function is null)
            throw new InvalidOperationException("VM: AST member is not compiled.");
        return VmCall.Invoke(state, function, a0, a1, a2, a3, a4, a5);
    }

    /// <inheritdoc cref="InvokeAstFrame(VmState, Action{VmState}, long)"/>
    private static long InvokeAstFrame(
        VmState state, Action<VmState> function,
        long a0, long a1, long a2, long a3, long a4, long a5, long a6) {
        if (function is null)
            throw new InvalidOperationException("VM: AST member is not compiled.");
        return VmCall.Invoke(state, function, a0, a1, a2, a3, a4, a5, a6);
    }

    /// <inheritdoc cref="InvokeAstFrame(VmState, Action{VmState}, long)"/>
    private static long InvokeAstFrame(
        VmState state, Action<VmState> function,
        long a0, long a1, long a2, long a3, long a4, long a5, long a6, long a7) {
        if (function is null)
            throw new InvalidOperationException("VM: AST member is not compiled.");
        return VmCall.Invoke(state, function, a0, a1, a2, a3, a4, a5, a6, a7);
    }

    /// <summary>
    /// The call site for a compiled AST member: <paramref name="function"/> with its
    /// argument words, receiver first. A narrow frame names its words directly; a wider
    /// one travels as a <c>long[]</c>. Both land in the same frame layout.
    /// </summary>
    private static Expression EmitCallFrame(
        Expression function, IReadOnlyList<Expression> words, Context ctx) {
        if (words.Count is > 0 and <= MaxDirectFrameWords) {
            var arguments = new List<Expression>(words.Count + 2) { ctx.State, function };
            arguments.AddRange(words);
            return Call(null, InvokeAstFrameArities[words.Count - 1], arguments);
        }
        return Call(null, InvokeAstFunctionInfo, ctx.State, function, NewArrayInit(typeof(long), words));
    }

    private static bool HasExecutableBody(Node? body) =>
        body is not null && body is not Block { Nodes.Count: 0 };

    /// <summary>
    /// Compiles the arguments of an AST constructor or method call (appended to
    /// <paramref name="seq"/>) and returns one callee frame value per parameter.
    /// A missing argument takes the parameter default. A stack scalar passed to a
    /// parameter that does not read a raw slot is heap-allocated, because the
    /// callee reads that slot as a heap handle. The storage convention is the
    /// definition's (<paramref name="declaredParameters"/>); the value comes from
    /// the caller's analysis of the argument node.
    /// </summary>
    private static List<Expression> EmitAstArguments(
        IReadOnlyList<Node> arguments, IReadOnlyList<Parameter> parameters,
        IReadOnlyList<IParameter> declaredParameters,
        string callee, List<Expression> seq, Context ctx) {
        if (arguments.Count > parameters.Count)
            throw new InvalidOperationException(
                $"VM compile rejected: {callee} has {parameters.Count} parameter(s) but the call has {arguments.Count} argument(s).");
        if (declaredParameters.Count != parameters.Count)
            throw new InvalidOperationException(
                $"VM compile rejected: {callee} parameter metadata does not match its declaration.");

        var values = new List<Expression>(parameters.Count);
        var ring = ctx.OpenRing();
        for (int i = 0; i < arguments.Count; i++) {
            var parameterType = ParameterClrType(parameters[i], ctx);
            if (IsExpressionBridgeParameter(parameterType)
                && TryBuildLambdaBridgeArgument(
                    arguments[i], parameterType, ctx,
                    out var bridgePrelude, out var bridgeArgument)) {
                ring.AddUncounted(bridgePrelude);
                int bridged = ring.AddValue(
                    RingCodec.ToFrame(bridgeArgument, parameterType ?? bridgeArgument.Type, ctx.HeapLocal));
                values.Add(ctx.RingVar(bridged));
                continue;
            }
            int got = ring.Add(CompileNode(arguments[i], ctx));
            values.Add(RingCodec.IsStackScalar(arguments[i], ctx.Analysis) && !ParameterReadsRawSlot(declaredParameters[i])
                ? Convert(
                    Call(ctx.HeapLocal, HeapAllocate, Convert(ctx.RingVar(got), typeof(object))),
                    typeof(long))
                : ctx.RingVar(got));
        }
        for (int i = arguments.Count; i < parameters.Count; i++)
            values.Add(DefaultArgument(parameters[i], declaredParameters[i], ctx));
        seq.AddRange(ring.Steps);
        return values;
    }

    /// <summary>A callee parameter's storage convention, from its declaration.
    /// An inline value type is a raw slot. A <c>Nullable&lt;T&gt;</c> is a handle:
    /// the callee unboxes it into a local.</summary>
    private static bool ParameterReadsRawSlot(IParameter parameter) {
        var runtimeType = parameter.ParameterTypeDefinition.GetRuntimeType();
        return runtimeType is not null
            && Nullable.GetUnderlyingType(runtimeType) is null
            && RingCodec.IsInline(runtimeType);
    }

    /// <summary>An omitted argument's default, stored by the callee parameter's
    /// convention: a raw slot for an inline value type, a heap handle otherwise.</summary>
    private static Expression DefaultArgument(Parameter parameter, IParameter declared, Context ctx) {
        if (ParameterReadsRawSlot(declared))
            return ParameterDefaultLong(parameter, ctx);
        var value = parameter.DefaultValue is Constant constant ? constant.Value : null;
        return Convert(
            Call(ctx.HeapLocal, HeapAllocate, Constant(value, typeof(object))),
            typeof(long));
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
            var instanceSlot = receiverRing.AddValue(CompileNode(member.Value, ctx));
            seq.AddRange(receiverRing.Steps);
            receiver = instanceSlot.Read;
        }

        var parameters = definition.Parameters ?? [];
        var args = EmitAstArguments(invoke.Arguments, parameters, method.Parameters.ToArray(),
            $"method '{method.Name}'", seq, ctx);
        var inits = new List<Expression>(parameters.Count + 1);
        if (receiver is not null)
            inits.Add(receiver);
        inits.AddRange(args);

        int result = ctx.AllocSlot();
        ctx.RingDepth = result + 1;
        // The callee already left the ring word at slots[FramePos] (VmCall.Invoke's
        // contract, void included), so the caller only moves it into its own slot.
        seq.Add(Assign(ctx.RingVar(result), EmitCallFrame(
            ArrayIndex(Constant(table), Constant(0)), inits, ctx)));
        return Block(seq);
    }

    /// <param name="rejection">Why the body did not compile, when it did not. Callers that
    /// keep a name-dispatched fallback carry this into the runtime failure, so a compile
    /// reject is never reported as a missing method on the receiver's storage type.</param>
    /// <summary>
    /// True when the receiver is an instance the VM allocates itself: <c>new T()</c> for a
    /// <c>T</c> analysis resolves to an AST type definition, whose runtime object is the
    /// VM's own dictionary. A host-supplied instance reaches the same call shape through
    /// <c>this</c> or a parameter instead, and may genuinely implement the method.
    /// </summary>
    private static bool IsVmConstructedInstance(Node receiver, Context ctx) =>
        receiver is New
        && ctx.Analysis?.GetResolvedType(receiver) is AstTypeDefinition;

    private static Action<VmState>[]? TryCompileAstCallable(
        Node key,
        Node body,
        IReadOnlyList<Parameter> parameters,
        Context caller,
        bool instanceSlot,
        out string? rejection,
        Type? asyncReturn = null) {
        rejection = null;
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
            var unitAnalysis = caller.Analysis ?? throw new InvalidOperationException(
                "VM compile rejected: emit requires the analysis of the compilation unit.");
            var bodyAnalysis = AnalysisForAstBody(unitAnalysis, key);
            if (bodyAnalysis.HasErrors) {
                var text = string.Join("; ", bodyAnalysis.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Take(8)
                    .Select(d => d.Message));
                throw new InvalidOperationException(
                    "VM compile rejected: AST member body analysis reported errors: " + text);
            }
            // Keep the full unit analysis on the frame. A nested callable must see
            // its own callee's diagnostics; handing it the narrowed copy would
            // make a dirty callee look clean.
            slot[0] = CompileFunctionBody(
                body,
                parameters,
                [],
                [],
                mode,
                unitAnalysis,
                new HashSet<object>(ReferenceEqualityComparer.Instance),
                instanceSlot,
                asyncReturn);
        }
        catch (Exception ex) {
            // Drop this slot and everything compiled under it: those bodies may
            // call this slot, which stays empty.
            pending.RemoveRange(start, pending.Count - start);
            if (ex is UnresumableBodyRejection)
                throw;
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
            rejection = ex.Message;
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

    /// <summary>The caller's compilation-unit analysis, narrowed to diagnostics
    /// that belong to this member. A typed Create forwarder shares its type with
    /// printed helpers the VM analyzer still flags (C8-vm); an error on another
    /// member must not reject this body. Emit consumes the unit's facts and never
    /// analyzes.</summary>
    private static AnalysisResult AnalysisForAstBody(AnalysisResult analysis, Node member) {
        if (!analysis.HasErrors)
            return analysis;
        var relevant = analysis.Diagnostics
            .Where(diagnostic => diagnostic.Node is { } node
                && (ReferenceEquals(node, member) || ContainsNode(member, node)))
            .ToArray();
        return analysis with {
            Diagnostics = relevant,
            HasErrors = relevant.Any(d => d.Severity == DiagnosticSeverity.Error)
        };
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

    private static readonly MethodInfo InvokeInstanceMethodInfo =
        Ref.Method((Expression<Func<object, string, object?[], string?, object?>>)((instance, name, args, bodyReject) =>
            InvokeInstanceMethod(instance, name, args, bodyReject)));
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
    /// <param name="bodyReject">Why the AST method body did not compile, when this call is a
    /// fallback for one. Carried into the failure so a compile reject is not reported as a
    /// missing method on the receiver's storage type.</param>
    private static object? InvokeInstanceMethod(
        object instance, string methodName, object?[] args, string? bodyReject = null) {
        if (instance is null)
            throw new InvalidOperationException(
                $"Invoke '{methodName}' requires a non-null instance."
                + Because(bodyReject));

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
            $"Type '{instanceType.Name}' does not define method '{methodName}' with {args.Length} parameter(s)."
            + Because(bodyReject));
    }

    /// <summary>The compile reject behind a name-dispatched fallback, as a sentence to
    /// append to the runtime failure — or nothing at all when there was no reject.</summary>
    private static string Because(string? bodyReject) =>
        bodyReject is null ? "" : $" (the AST method body did not compile: {Reason(bodyReject)})";

    /// <summary>A reject's text without the prefix its own thrower added, so a message that
    /// wraps another does not say "VM compile rejected" twice.</summary>
    private static string Reason(string? rejection) =>
        rejection is null ? "" : rejection.Replace("VM compile rejected: ", "", StringComparison.Ordinal).TrimEnd('.');

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

    /// <summary>The receiver of a resolved instance call. A temporal word is the
    /// value (<c>DateTime.AddDays</c> must not treat <c>ToBinary</c> as a heap
    /// index). Every other receiver is still the heap object.</summary>
    private static Expression InstanceCallTarget(
        Expression word, Type? receiverClr, Type declaringType, Context ctx) {
        var temporal = TemporalWord(receiverClr) ? receiverClr
            : TemporalWord(declaringType) ? declaringType
            : null;
        if (temporal is not null)
            return LocalType.Decode(word, temporal, declaringType, ctx.HeapLocal);
        return Convert(
            Call(ctx.HeapLocal, HeapUnsafeGet, Convert(word, typeof(int))),
            declaringType);
    }

    /// <summary>A name-dispatched receiver as <c>object</c>. A temporal word is
    /// decoded; a handle is still the heap object.</summary>
    private static Expression InstanceObject(Expression word, Type? clr, Context ctx) {
        if (TemporalWord(clr))
            return Convert(LocalType.Decode(word, clr, clr!, ctx.HeapLocal), typeof(object));
        return Call(ctx.HeapLocal, HeapUnsafeGet, Convert(word, typeof(int)));
    }

    /// <summary>A scalar argument of a name-dispatched call. A temporal word boxes
    /// the CLR value; an int or IEEE word boxes as it always has.</summary>
    private static Expression ScalarArgument(Expression word, Type? clr, Context ctx) {
        if (TemporalWord(clr))
            return Convert(LocalType.Decode(word, clr, clr!, ctx.HeapLocal), typeof(object));
        return Convert(word, typeof(object));
    }

    private static bool TemporalWord(Type? clr) =>
        clr is not null && LocalType.Of(clr) is LocalType.TemporalLocal { IsWord: true };

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