using System.Linq.Expressions;
using System.Reflection;

using Poly.Analysis;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

public static partial class DirectVmAbiEmitter {

    private static Expression EmitAssignment(Assignment a, AbiCtx ctx) {
        if (a.Destination is IndexAccess indexAccess) {
            var ring = ctx.OpenRing();
            int arrSlot = ring.Add(CompileNode(indexAccess.Value, ctx));
            int idxSlot = ring.Add(CompileNode(
                indexAccess.Arguments.Length > 0 ? indexAccess.Arguments[0] : new Constant(0), ctx));
            int valSlot = ring.Add(CompileNode(a.Value, ctx));

            Type? elemType = ctx.Analysis?.GetResolvedType(indexAccess) is ClrTypeDefinition clrElem
                ? clrElem.RuntimeType
                : null;
            var store = RingCodec.ArrayElementWrite(
                ctx.HeapLocal, ctx.RingVar(arrSlot), ctx.RingVar(idxSlot), ctx.RingVar(valSlot),
                elemType, RingCodec.IsIeee(a.Value, ctx.Analysis));

            ctx.RingDepth = arrSlot + 1;
            return ring.Then(Block(store, Assign(ctx.RingVar(arrSlot), ctx.RingVar(valSlot))));
        }

        if (a.Destination is Member member) {
            var resolved = ctx.Analysis?.GetResolvedMember(member);
            if (resolved is null)
                throw new NotSupportedException(
                    $"Assignment to unresolved member '{member.MemberName}'");

            var ring = ctx.OpenRing();
            int instanceSlot = ring.Add(CompileNode(member.Value, ctx));
            int valueSlot = ring.Add(CompileNode(a.Value, ctx));

            Expression instanceObj;
            var declaringTypeDef = resolved.DeclaringTypeDefinition;
            bool isInlineValueType = declaringTypeDef is ClrTypeDefinition clrDef
                && clrDef.RuntimeType.IsValueType
                && RingCodec.IsInline(clrDef.RuntimeType);
            if (isInlineValueType)
                instanceObj = Convert(ctx.RingVar(instanceSlot), typeof(object));
            else
                instanceObj = Call(ctx.HeapLocal,
                    HeapUnsafeGet,
                    Convert(ctx.RingVar(instanceSlot), typeof(int)));

            var memberTypeDef = resolved.MemberTypeDefinition;
            var clrMemberType = memberTypeDef.GetRuntimeType()
                ?? memberTypeDef.PrimitiveType?.GetClrType();
            Expression writeValue;
            if (clrMemberType is not null
                && clrMemberType.IsValueType
                && RingCodec.IsInline(clrMemberType)) {
                if (clrMemberType == typeof(bool))
                    writeValue = Condition(Equal(ctx.RingVar(valueSlot), Constant(0L)),
                        Constant(false, typeof(object)),
                        Constant(true, typeof(object)));
                else
                    writeValue = Convert(Convert(ctx.RingVar(valueSlot), clrMemberType), typeof(object));
            }
            else {
                writeValue = Call(ctx.HeapLocal,
                    HeapUnsafeGet,
                    Convert(ctx.RingVar(valueSlot), typeof(int)));
            }

            var writeExpr = resolved.EmitWrite(instanceObj, writeValue);
            var outSlot = ctx.AllocSlot();
            ctx.RingDepth = outSlot + 1;
            return ring.Then(Block(
                writeExpr ?? throw new NotSupportedException(
                    $"Member '{member.MemberName}' does not support EmitWrite"),
                Assign(ctx.RingVar(outSlot), ctx.RingVar(valueSlot))));
        }

        if (a.Destination is not Variable destVar) {
            throw new InvalidOperationException(
                $"VM compile rejected: assignment destination must be a Variable, Member, or IndexAccess, got {a.Destination.GetType().Name}.");
        }

        // Arrays are always heap-allocated. The former frame-local small-array encoding
        // (a base offset in the variable's slot) was not readable through a member access
        // (`.Length`) and did not survive every use; the heap path is the one representation.

        if (ctx.TryGetCapture(destVar, out int capIndex)) {
            var ring = ctx.OpenRing();
            int valSlot = ring.Add(CompileNode(a.Value, ctx));
            var store = Assign(EmitCaptureCellValue(ctx, capIndex), ctx.RingVar(valSlot));
            return ring.Then(store);
        }

        var value = ctx.OpenRing();
        int resultSlot = value.Add(CompileNode(a.Value, ctx));
        var result = ctx.VariableWrite(destVar,
            CoerceSlotToDestination(ctx, destVar, a.Value, ctx.RingVar(resultSlot)));
        return value.Then(result);
    }

    /// <summary>
    /// Convert a compiled value into the destination variable's slot representation.
    /// Doubles and floats live in slots as IEEE bit patterns, so a scalar value needs
    /// <c>DoubleToInt64Bits</c>, and a double value assigned to a scalar variable needs
    /// <c>Int64BitsToDouble</c> first. Without this the raw value lands in a slot the
    /// other representation reads (e.g. <c>double x = 1; x = 2;</c>).
    /// </summary>
    private static Expression CoerceSlotToDestination(AbiCtx ctx, Variable destination, Node value, Expression slot) {
        bool destinationIsDouble = RingCodec.IsIeee(destination, ctx.Analysis);
        bool valueIsDouble = RingCodec.IsIeee(value, ctx.Analysis);
        if (destinationIsDouble == valueIsDouble)
            return slot;
        return destinationIsDouble
            ? Call(BitConverterDoubleToInt64Bits, Convert(slot, typeof(double)))
            : Convert(Call(BitConverterInt64BitsToDouble, slot), typeof(long));
    }

    private static Expression EmitBlock(Block block, AbiCtx ctx) {
        ctx.PushScope();
        var varInitExprs = new List<Expression>();
        foreach (var v in block.Variables) {
            if (v is Variable variable) {
                ctx.DeclareVariable(variable);
                varInitExprs.Add(ctx.NeedsCell(variable)
                    ? EmitUpvalueCellInit(variable, ctx)
                    : Assign(ctx.VariableRead(variable), Constant(0L)));
            }
        }
        var stmtExprs = new List<Expression>();
        for (int i = 0; i < block.Nodes.Count; i++) {
            stmtExprs.Add(CompileStatement(block.Nodes[i], ctx));
        }
        var stores = ctx.EmitScopeStores();
        ctx.PopScope();
        var all = new List<Expression>(varInitExprs.Count + stmtExprs.Count + stores.Count);
        all.AddRange(varInitExprs);
        all.AddRange(stmtExprs);
        all.AddRange(stores);
        if (all.Count == 0) return Empty();
        return Block(all);
    }

    private static Expression EmitIfStatement(IfStatement ifStmt, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var test = CompileConditionAsBool(ifStmt.Condition, ctx);
        ctx.RingDepth = d;
        var thenBody = CompileNode(ifStmt.ThenBranch, ctx);
        ctx.RingDepth = d;
        Expression elseBody = Empty();
        if (ifStmt.ElseBranch is not null) {
            elseBody = CompileNode(ifStmt.ElseBranch, ctx);
            ctx.RingDepth = d;
        }
        return IfThenElse(test, thenBody, elseBody);
    }

    private static Expression EmitConditional(Conditional c, AbiCtx ctx) =>
        SpillToRing(EmitConditionalValue(c, ctx), ctx);

    private static Expression EmitCoalesce(Coalesce n, AbiCtx ctx) =>
        SpillToRing(EmitCoalesceValue(n, ctx), ctx);

    private static Expression EmitSwitch(SwitchStatement sw, AbiCtx ctx) {
        var ring = ctx.OpenRing();
        int valSlot = ring.Add(CompileNode(sw.Value, ctx));
        int savedDepth = ring.Base + 1;
        ctx.RingDepth = savedDepth;

        int outSlot = ctx.AllocSlot();
        ctx.RingDepth = savedDepth;

        Expression rest;
        if (sw.DefaultCase != null) {
            var defBody = CompileNode(sw.DefaultCase, ctx);
            int defSlot = ctx.RingDepth - 1;
            rest = Block(defBody, Assign(ctx.RingVar(outSlot), ctx.RingVar(defSlot)));
        }
        else {
            rest = Assign(ctx.RingVar(outSlot), Constant(0L));
        }

        for (int i = sw.Cases.Count - 1; i >= 0; i--) {
            ctx.RingDepth = savedDepth;
            var c = sw.Cases[i];
            var pExpr = CompileNode(c.Pattern, ctx);
            int pSlot = ctx.RingDepth - 1;
            ctx.RingDepth = savedDepth;
            var bExpr = CompileNode(c.Body, ctx);
            int bSlot = ctx.RingDepth - 1;
            rest = Block(
                pExpr,
                IfThenElse(
                    Equal(ctx.RingVar(valSlot), ctx.RingVar(pSlot)),
                    Block(bExpr, Assign(ctx.RingVar(outSlot), ctx.RingVar(bSlot))),
                    rest));
        }

        ctx.RingDepth = outSlot + 1;
        return ring.Then(Block(rest, ctx.RingVar(outSlot)));
    }

    private static Expression EmitWhileLoop(WhileLoop wl, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var breakLabel = Label("wl_break");
        var continueLabel = Label("wl_continue");
        ctx.PushLoopScope(breakLabel, continueLabel, wl.Label);
        var test = CompileConditionAsBool(wl.Condition, ctx);
        ctx.RingDepth = d;
        var bodyExpr = CompileNode(wl.Body, ctx);
        ctx.RingDepth = d;
        var loopBody = Block(
            EmitLoopIterationGuard(ctx),
            IfThen(Not(test), Goto(breakLabel)),
            bodyExpr,
            Label(continueLabel));
        var result = Loop(loopBody, breakLabel);
        ctx.PopLoopScope();
        ctx.RingDepth = d;
        return result;
    }

    private static Expression EmitLoopIterationGuard(AbiCtx ctx) {
        if (ctx.Mode == CompilationMode.NoDebug)
            return Empty();
        var max = Property(ctx.State, nameof(VmState.MaxLoopIterations));
        var ticks = Property(ctx.State, nameof(VmState.LoopTicks));
        return IfThen(
            GreaterThanOrEqual(max, Constant(0L)),
            Block(
                Assign(ticks, Add(ticks, Constant(1L))),
                IfThen(
                    GreaterThan(ticks, max),
                    Throw(New(InvalidOperationExceptionStringCtor,
                        Constant("MaxLoopIterations exceeded."))))));
    }

    private static Expression EmitDoWhileLoop(DoWhileLoop dwl, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var breakLabel = Label("dwl_break");
        var continueLabel = Label("dwl_continue");
        ctx.PushLoopScope(breakLabel, continueLabel, dwl.Label);
        var bodyExpr = CompileNode(dwl.Body, ctx);
        ctx.RingDepth = d;
        var test = CompileConditionAsBool(dwl.Condition, ctx);
        ctx.RingDepth = d;
        var loopBody = Block(
            EmitLoopIterationGuard(ctx),
            bodyExpr,
            Label(continueLabel),
            IfThen(Not(test), Goto(breakLabel)));
        var result = Loop(loopBody, breakLabel);
        ctx.PopLoopScope();
        ctx.RingDepth = d;
        return result;
    }

    private static Expression EmitThrow(ThrowStatement ts, AbiCtx ctx) {
        // Preserve the operand instance (Constant / Variable / New / ThrowExpression value).
        // Match LINQ: Expression.Throw(CompileNode(exception)).
        var compiled = CompileNode(ts.Exception, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var heapObj = Call(ctx.HeapLocal,
            HeapUnsafeGet,
            Convert(ctx.RingVar(resultSlot), typeof(int)));
        var exVar = Variable(typeof(Exception), "_thrownEx");
        return Block(
            [exVar],
            compiled,
            Assign(exVar, Convert(heapObj, typeof(Exception))),
            Throw(exVar));
    }

    private static Expression EmitTryCatchFinally(TryCatchFinally tcf, AbiCtx ctx) {
        var tryBody = CompileNode(tcf.TryBlock, ctx);
        Expression? finallyBody = null;
        if (tcf.FinallyBlock != null) {
            finallyBody = CompileNode(tcf.FinallyBlock, ctx);
        }
        if (tcf.CatchClauses == null || tcf.CatchClauses.Count == 0) {
            return finallyBody != null ? TryFinally(tryBody, finallyBody) : tryBody;
        }
        // Catch bodies are void. A value-producing try (an assignment, an await)
        // has to match, or Expression.TryCatch rejects the tree.
        if (tryBody.Type != typeof(void))
            tryBody = Block(tryBody, Empty());
        var catchBlocks = new List<CatchBlock>();
        foreach (var clause in tcf.CatchClauses) {
            var exType = ResolveCatchClrType(clause.ExceptionType, ctx);
            var exParam = Parameter(exType, clause.VariableName ?? "ex");
            ctx.PushScope();
            Variable? synthetic = null;
            if (!string.IsNullOrEmpty(clause.VariableName)) {
                synthetic = new Variable(clause.VariableName);
                ctx.DeclareVariable(synthetic);
            }
            var bodyExpr = CompileNode(clause.Body, ctx);
            var allocate = Call(ctx.HeapLocal, HeapAllocate, Convert(exParam, typeof(object)));
            var handle = Convert(allocate, typeof(long));
            Expression catchBodyExpr = bodyExpr;
            if (synthetic != null) {
                catchBodyExpr = Block(
                    ctx.VariableWrite(synthetic, handle),
                    bodyExpr
                );
            }
            var catchStores = ctx.EmitScopeStores();
            if (catchStores.Count > 0)
                catchBodyExpr = Block(catchBodyExpr, Block(catchStores));
            ctx.PopScope();
            catchBlocks.Add(Catch(exParam,
                catchBodyExpr.Type == typeof(void)
                    ? catchBodyExpr
                    : Block(catchBodyExpr, Empty())));
        }
        if (finallyBody != null) {
            return TryCatchFinally(tryBody, finallyBody, catchBlocks.ToArray());
        }
        return TryCatch(tryBody, catchBlocks.ToArray());
    }

    private static Expression EmitSuspendNode(SuspendNode sn, AbiCtx ctx) {
        int step = ctx.StepCounter++;
        ctx.RecordStepNode(step, sn);
        var resumeLabel = ctx.RegisterOrGetResumeLabel(step);
        var innerExpr = CompileNode(sn.Inner, ctx);
        var setCurrentNode = Block(
            Assign(Property(ctx.State, nameof(VmState.CurrentAstNode)), Constant(sn)),
            Assign(Property(ctx.State, nameof(VmState.CurrentNodeId)), Constant(sn.Id, typeof(NodeId?))));
        var setStatus = Assign(
            Property(ctx.State, nameof(VmState.Status)),
            Constant(InterpreterStatus.Suspended));
        // Flush step to VmState.ProgramCounter — EmitPcDispatch switches on state, not local _pc.
        var saveResumeId = Assign(ctx.StatePcFlush, Constant(step));
        var saveFramePos = Assign(
            Property(ctx.State, nameof(VmState.FramePos)),
            ctx.FramePosLocal);
        var clearSuspended = Assign(
            Property(ctx.State, nameof(VmState.Status)),
            Constant(InterpreterStatus.Running));
        // Resume label is AFTER suspend+exit so Resume falls through to later statements.
        return Block(
            innerExpr,
            setCurrentNode,
            setStatus,
            saveResumeId,
            saveFramePos,
            Goto(ctx.ExitLabel),
            Label(resumeLabel),
            clearSuspended);
    }

    private static Expression EmitParameter(Parameter p, AbiCtx ctx) {
        int slot = ctx.AllocSlot();
        if (ctx.TryGetCapture(p, out int capIndex)) {
            return Assign(ctx.RingVar(slot), EmitCaptureCellValue(ctx, capIndex));
        }
        return Assign(ctx.RingVar(slot), ParameterValue(p, ctx));
    }

    private static Expression ParameterValue(Parameter p, AbiCtx ctx) {
        if (ctx.TryGetInlineParameter(p, out int ringSlot))
            return ctx.RingVar(ringSlot);
        if (!ctx.TryGetParameterSlot(p, out int paramIdx))
            paramIdx = ctx.DeclareParameter(p);
        // Pre-C1a root programs: param 0 aliases SetArgs(this) via InstanceHandle.
        // C1a roots set IsCompiledFunctionBody and declare real params at slots 1+.
        if (paramIdx == 0 && ctx.ParamSlotOffset == 0 && !ctx.HasInlineParameters
            && !ctx.IsCompiledFunctionBody)
            return ctx.InstanceHandle;
        return ctx.ParameterRead(paramIdx);
    }

    /// <summary>
    /// Domain programs bind the instance via <c>SetArgs(this, …)</c> at slot 0.
    /// After SetArgs, ThisReference is that handle. Declared parameters follow at
    /// slot 1+. Unset slot 0 is ABI null 0.
    /// </summary>
    private static Expression EmitThis(AbiCtx ctx) {
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot), ctx.InstanceHandle);
    }

    private static Expression EmitCaptureCell(AbiCtx ctx, int capIndex) =>
        RingCodec.ClosureCell(ctx.HeapLocal, ctx.ClosureHandle, capIndex);

    private static Expression EmitCaptureCellValue(AbiCtx ctx, int capIndex) =>
        ArrayAccess(EmitCaptureCell(ctx, capIndex), Constant(0));

    private static Expression EmitUpvalueCellInit(Variable v, AbiCtx ctx) {
        ctx.MarkCellBacked(v);
        var cellVar = Variable(typeof(long[]), "_upvalue");
        return Block(
            [cellVar],
            Assign(cellVar, NewArrayBounds(typeof(long), Constant(1))),
            Assign(ArrayAccess(cellVar, Constant(0)), Constant(0L)),
            ctx.VariableWriteRaw(v,
                Convert(Call(ctx.HeapLocal, HeapAllocate, Convert(cellVar, typeof(object))), typeof(long))));
    }

    private static Expression EmitLambda(Lambda lambda, AbiCtx ctx) {
        if (lambda.LambdaIndex < 0)
            throw new InvalidOperationException("Lambda.LambdaIndex not set during lambda collection");
        var captures = FindBodyCaptures(lambda, ctx.Analysis);
        int arrLen = 1 + captures.Count;
        var closureArrVar = Variable(typeof(object[]), "_closureArr");
        var locals = new List<ParameterExpression> { closureArrVar };
        var body = new List<Expression>();
        body.Add(Assign(closureArrVar, NewArrayBounds(typeof(object), Constant(arrLen))));
        body.Add(Assign(ArrayAccess(closureArrVar, Constant(RingCodec.ClosureIndexSlot)),
            Convert(Constant((long)lambda.LambdaIndex), typeof(object))));
        for (int i = 0; i < captures.Count; i++) {
            var cellVar = Variable(typeof(long[]), $"_upvalue{i}");
            locals.Add(cellVar);
            body.Add(EmitLoadOrCreateUpvalueCell(captures[i], ctx, cellVar));
            body.Add(Assign(ArrayAccess(closureArrVar, Constant(RingCodec.FirstCaptureIndex + i)),
                Convert(cellVar, typeof(object))));
        }
        var handle = Call(ctx.HeapLocal, HeapAllocate,
            Convert(closureArrVar, typeof(object)));
        int slot = ctx.AllocSlot();
        body.Add(Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));
        return Block(locals, body);
    }

    private static Expression EmitLoadOrCreateUpvalueCell(
        Capture cap, AbiCtx ctx, ParameterExpression cellVar) {
        if (ctx.TryGetCapture(cap.Binding, out int capIndex))
            return Assign(cellVar, EmitCaptureCell(ctx, capIndex));
        if (cap.Variable is { } v) {
            if (!ctx.IsCellBacked(v))
                throw new InvalidOperationException(
                    $"VM compile rejected: captured variable '{v.Name}' has no upvalue cell.");
            return Assign(cellVar, RingCodec.CellArray(ctx.HeapLocal, ctx.VariableReadRaw(v)));
        }
        return Block(
            Assign(cellVar, NewArrayBounds(typeof(long), Constant(1))),
            Assign(ArrayAccess(cellVar, Constant(0)), ReadCaptureSource(cap, ctx)));
    }

    private static void DeclareFreeParameters(Node node, AbiCtx ctx, HashSet<Parameter> ownParams) {
        if (node is Parameter p
            && !ownParams.Contains(p)
            && ownParams.All(op => op.Name != p.Name)
            && !ctx.TryGetParameterSlot(p, out _))
            ctx.DeclareParameter(p);
        foreach (var child in node.Children) {
            if (child is not null)
                DeclareFreeParameters(child, ctx, ownParams);
        }
    }

    private static List<Capture> FindBodyCaptures(Lambda lambda, AnalysisResult? analysis) {
        var bindings = analysis?.GetMetadata<LambdaCaptureMetadata>(lambda)?.Bindings
            ?? LambdaCaptureCollector.Collect(lambda, n => analysis?.GetNodeReplacement(n));
        var fromAnalysis = new List<Capture>(bindings.Count);
        foreach (var b in bindings)
            fromAnalysis.Add(new Capture(b.Variable, b.Parameter));
        return fromAnalysis;
    }

    private static Expression ReadCaptureSource(Capture cap, AbiCtx ctx) {
        if (cap.Variable is not null)
            return ctx.VariableRead(cap.Variable);
        return ParameterValue(cap.Parameter!, ctx);
    }

    /// <summary>Break statement: jump to the named loop, or the innermost if unlabeled.</summary>
    private static Expression EmitBreakStatement(BreakStatement bs, AbiCtx ctx) =>
        Goto(ctx.ResolveLoopLabels(bs.Label).breakLabel);

    /// <summary>Continue statement: jump to the named loop's continue, or the innermost if unlabeled.</summary>
    private static Expression EmitContinueStatement(ContinueStatement cs, AbiCtx ctx) =>
        Goto(ctx.ResolveLoopLabels(cs.Label).continueLabel);

    /// <summary>
    /// ForLoop: lower to { init; while(condition) { body; increment; } }
    /// When condition is null, treat as 'while(true)'.
    /// </summary>
    private static Expression EmitForLoop(ForLoop fl, AbiCtx ctx) {
        var breakLabel = Label("for_break");
        var continueLabel = Label("for_continue");
        ctx.PushLoopScope(breakLabel, continueLabel, fl.Label);

        // A declaration initializer (`for (var i = 0; …)`) owns a scope: allocate its
        // variables before the initializer writes them, and release the slots when the
        // loop ends. The plain assignment form declares nothing here.
        var declared = fl.DeclaredVariables;
        var scoped = declared is { Count: > 0 };
        if (scoped)
            ctx.PushScope();

        var stmts = new List<Expression>();
        if (declared is { Count: > 0 }) {
            foreach (var v in declared) {
                ctx.DeclareVariable(v);
                if (ctx.NeedsCell(v))
                    stmts.Add(EmitUpvalueCellInit(v, ctx));
            }
        }
        if (fl.Initializer != null)
            stmts.Add(CompileNode(fl.Initializer, ctx));
        var condition = fl.Condition ?? new Constant(1L);
        int d = ctx.RingDepth;

        int bodyDepth = ctx.RingDepth;
        var bodyExpr = CompileNode(fl.Body, ctx);
        ctx.RingDepth = bodyDepth;

        Expression? incrementExpr = null;
        if (fl.Increment != null) {
            ctx.RingDepth = d;
            incrementExpr = CompileNode(fl.Increment, ctx);
            ctx.RingDepth = d;
        }

        var test = CompileConditionAsBool(condition, ctx);
        ctx.RingDepth = d;

        var loopBody = new List<Expression> {
            EmitLoopIterationGuard(ctx),
            IfThen(Not(test), Goto(breakLabel)),
            bodyExpr,
            Label(continueLabel)
        };
        if (incrementExpr != null) loopBody.Add(incrementExpr);

        stmts.Add(Loop(Block(loopBody), breakLabel));
        if (scoped)
            ctx.PopScope();
        ctx.PopLoopScope();
        ctx.RingDepth = d;
        return Block(stmts);
    }

    /// <summary>
    /// ForEachLoop: walk a heap <see cref="System.Collections.IEnumerable"/>,
    /// bind each Current onto the loop variable via <c>BoxToAbi</c>, run the body.
    /// Null / non-enumerable fails loud. Enumerator is disposed when IDisposable.
    /// </summary>
    private static Expression EmitForEachLoop(ForEachLoop fel, AbiCtx ctx) {
        var breakLabel = Label("foreach_break");
        var continueLabel = Label("foreach_continue");
        ctx.PushLoopScope(breakLabel, continueLabel, fel.Label);
        ctx.PushScope();
        ctx.DeclareVariable(fel.LoopVariable);
        Expression loopVarCellInit = ctx.NeedsCell(fel.LoopVariable)
            ? EmitUpvalueCellInit(fel.LoopVariable, ctx)
            : Empty();

        var ring = ctx.OpenRing();
        int colSlot = ring.Add(CompileNode(fel.Collection, ctx));

        var enumerator = Variable(typeof(System.Collections.IEnumerator), "_en");
        var colObj = Variable(typeof(object), "_col");

        int bodyDepth = ctx.RingDepth;
        var bodyExpr = CompileNode(fel.Body, ctx);
        ctx.RingDepth = bodyDepth;

        var loop = Loop(
            Block(
                EmitLoopIterationGuard(ctx),
                IfThen(Not(Call(enumerator, EnumeratorMoveNext)),
                    Goto(breakLabel)),
                ctx.VariableWrite(fel.LoopVariable, ForEachElementValue(fel, enumerator, ctx)),
                bodyExpr,
                Label(continueLabel)),
            breakLabel);

        ctx.PopLoopScope();
        ctx.PopScope();
        ctx.RingDepth = ring.Base;

        return ring.Then(Block(
            [enumerator, colObj],
            loopVarCellInit,
            Assign(colObj, Call(ctx.HeapLocal, HeapUnsafeGet,
                Convert(ctx.RingVar(colSlot), typeof(int)))),
            Assign(enumerator, Call(null, GetEnumeratorOrThrowInfo, colObj)),
            TryFinally(
                loop,
                IfThen(
                    TypeIs(enumerator, typeof(IDisposable)),
                    Call(Convert(enumerator, typeof(IDisposable)), IDisposableDispose)))));
    }

    /// <summary>The ring value stored into the loop variable each iteration. Without a declared
    /// type the element keeps its own representation (<see cref="BoxToAbiInfo"/>); with one, the
    /// element is unboxed to its element type, converted to the declared type, and encoded by that
    /// type's representation — a value widened into an IEEE slot would otherwise be mis-read, and a
    /// value stored where the declared type is a reference must become a heap handle.</summary>
    private static Expression ForEachElementValue(ForEachLoop fel, ParameterExpression enumerator, AbiCtx ctx) {
        var current = Property(enumerator, EnumeratorCurrent);
        if (fel.DeclaredType is null)
            return Call(null, BoxToAbiInfo, ctx.HeapLocal, current);

        var declaredClr = ctx.Analysis?.GetResolvedType(fel.DeclaredType)?.GetRuntimeType();
        if (declaredClr is null)
            throw new InvalidOperationException(
                $"VM compile rejected: cannot resolve the foreach declared type '{fel.DeclaredType}'.");
        var elementClr = ctx.Analysis?.GetMetadata<ForEachElementTypeMetadata>(fel)?.ElementType?.GetRuntimeType();
        if (elementClr is null)
            throw new InvalidOperationException(
                $"VM compile rejected: cannot resolve the element type of the foreach collection '{fel.Collection}'.");

        var slot = Call(null, BoxToAbiInfo, ctx.HeapLocal, current);
        if (!IsScalarSlot(elementClr) || !IsScalarSlot(declaredClr))
            throw new InvalidOperationException(
                $"VM compile rejected: cannot convert each '{elementClr.Name}' element to the foreach loop type '{declaredClr.Name}'.");
        bool elementIeee = elementClr == typeof(double) || elementClr == typeof(float);
        bool declaredIeee = declaredClr == typeof(double) || declaredClr == typeof(float);
        if (declaredIeee == elementIeee)
            return slot; // both scalars in the same slot encoding
        return declaredIeee
            ? Call(BitConverterDoubleToInt64Bits, Convert(slot, typeof(double)))
            : Convert(Call(BitConverterInt64BitsToDouble, slot), typeof(long));
    }

    /// <summary>True for a type that lives in a scalar ring slot (bool or a long-representable
    /// value type, plus IEEE float/double). <c>decimal</c> and reference types do not.</summary>
    private static bool IsScalarSlot(Type t) =>
        t == typeof(double) || t == typeof(float) || t == typeof(bool)
        || (t.IsValueType && RingCodec.IsInline(t));

    /// <summary>Goto statement: jump to a named label.</summary>
    private static Expression EmitGotoStatement(GotoStatement gs, AbiCtx ctx) {
        var target = ctx.GetLabel(gs.Target);
        return Goto(target);
    }

    /// <summary>Label declaration: emit a label marker in the expression tree.</summary>
    private static Expression EmitLabelDeclaration(LabelDeclaration ld, AbiCtx ctx) {
        var label = ctx.GetLabel(ld.Name);
        return Block(Label(label), CompileNode(ld.Statement, ctx));
    }

    /// <summary>UsingStatement: try/finally Dispose when the resource is IDisposable.</summary>
    private static Expression EmitUsingStatement(UsingStatement us, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var resourceExpr = CompileNode(us.Resource, ctx);
        int resSlot = ctx.RingDepth - 1;
        var bodyExpr = CompileNode(us.Body, ctx);
        ctx.RingDepth = d;
        var resObj = Variable(typeof(object), "_using");
        return Block(
            [resObj],
            resourceExpr,
            Assign(resObj, Call(ctx.HeapLocal, HeapUnsafeGet,
                Convert(ctx.RingVar(resSlot), typeof(int)))),
            TryFinally(
                bodyExpr,
                IfThen(
                    TypeIs(resObj, typeof(IDisposable)),
                    Call(Convert(resObj, typeof(IDisposable)), IDisposableDispose))));
    }

    private static Type ResolveCatchClrType(Node? exceptionType, AbiCtx ctx) {
        if (exceptionType is null) return typeof(Exception);
        if (exceptionType is ClrTypeReference ctr
            && typeof(Exception).IsAssignableFrom(ctr.RuntimeType))
            return ctr.RuntimeType;
        var rt = ctx.Analysis?.GetResolvedType(exceptionType)?.GetRuntimeType();
        if (rt is not null && typeof(Exception).IsAssignableFrom(rt))
            return rt;
        return typeof(Exception);
    }

    /// <summary>Return statement: write value to frame slot, set SP, jump to exit.
    /// A null <see cref="Return.Value"/> is a void return (no slot write).</summary>
    private static Expression EmitReturn(Return ret, AbiCtx ctx) {
        if (ret.Value is null) {
            if (ctx.AsyncReturn is not null)
                return Block(
                    Assign(ArrayAccess(ctx.SlotsLocal, ctx.FramePosLocal),
                        WrapAsyncResult(null, ctx.AsyncReturn, ctx, null)),
                    Assign(ctx.SlotsStackPointer,
                        Add(ctx.FramePosLocal, Constant(1))),
                    Goto(ctx.ExitLabel));
            return Goto(ctx.ExitLabel);
        }

        var ring = ctx.OpenRing();
        int resultSlot = ring.Add(CompileNode(ret.Value, ctx));
        Expression value = ctx.RingVar(resultSlot);
        if (ctx.AsyncReturn is not null)
            value = WrapAsyncResult(value, ctx.AsyncReturn, ctx, ret.Value);

        return ring.Then(Block(
            Assign(ArrayAccess(ctx.SlotsLocal, ctx.FramePosLocal), value),
            Assign(ctx.SlotsStackPointer,
                Add(ctx.FramePosLocal, Constant(1))),
            Goto(ctx.ExitLabel)));
    }
}