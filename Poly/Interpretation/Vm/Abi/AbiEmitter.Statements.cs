using System.Linq.Expressions;
using System.Reflection;

using Poly.Analysis;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

public static partial class AbiEmitter {

    private static Expression EmitAssignment(Assignment a, Context ctx) {
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

        var destination = ctx.Resolve(destVar)
            ?? throw new InvalidOperationException(
                $"VM compile rejected: assignment destination '{destVar.Name}' is not declared in any scope.");
        var value = ctx.OpenRing();
        int resultSlot = value.Add(CompileNode(a.Value, ctx));
        var result = destination.Write(ctx,
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
    private static Expression CoerceSlotToDestination(Context ctx, Variable destination, Node value, Expression slot) {
        bool destinationIsDouble = RingCodec.IsIeee(destination, ctx.Analysis);
        bool valueIsDouble = RingCodec.IsIeee(value, ctx.Analysis);
        if (destinationIsDouble == valueIsDouble)
            return slot;
        return destinationIsDouble
            ? RingCodec.ToIeee(Convert(slot, typeof(double)))
            : Convert(RingCodec.FromIeee(slot), typeof(long));
    }

    private static Expression EmitBlock(Block block, Context ctx) {
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
        // Each statement starts from the block's entry depth. A statement's result is
        // observable only when it is the block's value, so the earlier ones must not
        // hold ring slots after they run — that was the ambient RingDepth cursor
        // carrying values past their death (nqueens emitted 23 ring locals for a body
        // whose deepest statement needs a handful). The last statement keeps its depth,
        // so the block's value stays at RingDepth - 1 and Compile reports it.
        var stmtExprs = new List<Expression>(block.Nodes.Count);
        int entryDepth = ctx.RingDepth;
        for (int i = 0; i < block.Nodes.Count; i++) {
            var statement = block.Nodes[i];
            // A comment or a label emits no code and no value, so it must not release
            // what the previous statement left: that value is still the block's result
            // (`[return x, comment]` yields x).
            if (statement is not (Comment or LabelDeclaration))
                ctx.RingDepth = entryDepth;
            stmtExprs.Add(CompileStatement(statement, ctx));
        }
        var stores = ctx.EmitScopeStores();
        ctx.PopScope();
        var all = new List<Expression>(varInitExprs.Count + stmtExprs.Count + stores.Count);
        all.AddRange(varInitExprs);
        all.AddRange(stmtExprs);
        // The block-exit snapshot is also what VmDebugger.GetLocals reads after
        // execution, when no hook was ever attached, so it stays unconditional.
        // Per-statement stores live inside the hook guard (CompileStatement).
        all.AddRange(stores);
        if (all.Count == 0) return Empty();
        return Block(all);
    }

    private static Expression EmitIfStatement(IfStatement ifStmt, Context ctx) {
        // Every arm starts from the same depth and its slots are dead afterwards, so
        // the scope restores it instead of a remembered reset after each arm.
        Expression test;
        using (ctx.OpenRingScope()) {
            test = CompileConditionAsBool(ifStmt.Condition, ctx);
        }
        Expression thenBody;
        using (ctx.OpenRingScope()) {
            thenBody = CompileNode(ifStmt.ThenBranch, ctx);
        }
        Expression elseBody = Empty();
        if (ifStmt.ElseBranch is not null) {
            using (ctx.OpenRingScope()) {
                elseBody = CompileNode(ifStmt.ElseBranch, ctx);
            }
        }
        return IfThenElse(test, thenBody, elseBody);
    }

    private static Expression EmitConditional(Conditional c, Context ctx) =>
        SpillToRing(EmitConditionalValue(c, ctx), ctx);

    private static Expression EmitCoalesce(Coalesce n, Context ctx) =>
        SpillToRing(EmitCoalesceValue(n, ctx), ctx);

    private static Expression EmitSwitch(SwitchStatement sw, Context ctx) {
        var ring = ctx.OpenRing();
        int valSlot = ring.Add(CompileNode(sw.Value, ctx));
        int savedDepth = ring.Base + 1;

        int outSlot = ctx.AllocSlot();
        // Every arm is compiled from the slot after the value, and a scope restores to
        // it, so one write bases the whole switch on the same region.
        ctx.RingDepth = savedDepth;

        Expression rest;
        if (sw.DefaultCase != null) {
            CompiledNode def;
            using (ctx.OpenRingScope()) {
                def = Compile(sw.DefaultCase, ctx);
            }
            rest = Block(def.Code,
                Assign(ctx.RingVar(outSlot), def.RequireResult(sw.DefaultCase).Read(ctx)));
        }
        else {
            rest = Assign(ctx.RingVar(outSlot), Constant(0L));
        }

        for (int i = sw.Cases.Count - 1; i >= 0; i--) {
            var c = sw.Cases[i];
            // The arm's value is a named location, so the cursor never has to be
            // read back as "the slot the last compile left".
            CompiledNode pattern;
            using (ctx.OpenRingScope()) {
                pattern = Compile(c.Pattern, ctx);
            }
            CompiledNode body;
            using (ctx.OpenRingScope()) {
                body = Compile(c.Body, ctx);
            }
            rest = Block(
                pattern.Code,
                IfThenElse(
                    Equal(ctx.RingVar(valSlot), pattern.RequireResult(c.Pattern).Read(ctx)),
                    Block(body.Code,
                        Assign(ctx.RingVar(outSlot), body.RequireResult(c.Body).Read(ctx))),
                    rest));
        }

        ctx.RingDepth = outSlot + 1;
        return ring.Then(Block(rest, ctx.RingVar(outSlot)));
    }

    private static Expression EmitWhileLoop(WhileLoop wl, Context ctx) {
        var breakLabel = Label("wl_break");
        var continueLabel = Label("wl_continue");
        ctx.PushLoopScope(breakLabel, continueLabel, wl.Label);
        Expression test;
        using (ctx.OpenRingScope()) {
            test = CompileConditionAsBool(wl.Condition, ctx);
        }
        Expression bodyExpr;
        using (ctx.OpenRingScope()) {
            bodyExpr = CompileNode(wl.Body, ctx);
        }
        var loopBody = Block(
            EmitLoopIterationGuard(ctx),
            IfThen(Not(test), Goto(breakLabel)),
            bodyExpr,
            Label(continueLabel));
        var result = Loop(loopBody, breakLabel);
        ctx.PopLoopScope();
        return result;
    }

    private static Expression EmitLoopIterationGuard(Context ctx) {
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

    private static Expression EmitDoWhileLoop(DoWhileLoop dwl, Context ctx) {
        var breakLabel = Label("dwl_break");
        var continueLabel = Label("dwl_continue");
        ctx.PushLoopScope(breakLabel, continueLabel, dwl.Label);
        Expression bodyExpr;
        using (ctx.OpenRingScope()) {
            bodyExpr = CompileNode(dwl.Body, ctx);
        }
        Expression test;
        using (ctx.OpenRingScope()) {
            test = CompileConditionAsBool(dwl.Condition, ctx);
        }
        var loopBody = Block(
            EmitLoopIterationGuard(ctx),
            bodyExpr,
            Label(continueLabel),
            IfThen(Not(test), Goto(breakLabel)));
        var result = Loop(loopBody, breakLabel);
        ctx.PopLoopScope();
        return result;
    }

    private static Expression EmitThrow(ThrowStatement ts, Context ctx) {
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

    private static Expression EmitTryCatchFinally(TryCatchFinally tcf, Context ctx) {
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

    private static Expression EmitSuspendNode(SuspendNode sn, Context ctx) {
        if (!ctx.IsRootBody)
            throw new UnresumableBodyRejection(
                $"VM compile rejected: SuspendNode '{sn}' is only executable in the compilation-unit body; a compiled function or lambda body emits no resume dispatch.");
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

    private static Expression EmitParameter(Parameter p, Context ctx) {
        int slot = ctx.AllocSlot();
        if (ctx.TryGetCapture(p, out int capIndex)) {
            return Assign(ctx.RingVar(slot), EmitCaptureCellValue(ctx, capIndex));
        }
        return Assign(ctx.RingVar(slot), ParameterValue(p, ctx));
    }

    private static Expression ParameterValue(Parameter p, Context ctx) =>
        ctx.Resolve(p).Read(ctx);

    /// <summary>
    /// Domain programs bind the instance via <c>SetArgs(this, …)</c> at slot 0.
    /// After SetArgs, ThisReference is that handle. Declared parameters follow at
    /// slot 1+. Unset slot 0 is ABI null 0.
    /// </summary>
    private static Expression EmitThis(Context ctx) {
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot), ctx.InstanceHandle);
    }

    private static Expression EmitCaptureCell(Context ctx, int capIndex) =>
        ctx.CaptureCellArray(capIndex);

    private static Expression EmitCaptureCellValue(Context ctx, int capIndex) =>
        new SlotLocation.Cell(ctx.CaptureCellArray(capIndex)).Read(ctx);

    private static Expression EmitUpvalueCellInit(Variable v, Context ctx) {
        ctx.MarkCellBacked(v);
        var cellVar = Variable(typeof(long[]), "_upvalue");
        return Block(
            [cellVar],
            Assign(cellVar, NewArrayBounds(typeof(long), Constant(1))),
            Assign(ArrayAccess(cellVar, Constant(0)), Constant(0L)),
            ctx.VariableWriteRaw(v,
                Convert(Call(ctx.HeapLocal, HeapAllocate, Convert(cellVar, typeof(object))), typeof(long))));
    }

    private static Expression EmitLambda(Lambda lambda, Context ctx) {
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
        Capture cap, Context ctx, ParameterExpression cellVar) {
        if (ctx.TryGetCapture(cap.Binding, out int capIndex))
            return Assign(cellVar, EmitCaptureCell(ctx, capIndex));
        if (cap.Variable is { } v) {
            // A reader emits a Variable for a module parameter use; the bind pass
            // links it to the Parameter, which is captured by value into a fresh cell.
            if (ctx.BoundParameter(v) is { } bound) {
                if (ctx.TryGetCapture(bound, out int boundCap))
                    return Assign(cellVar, EmitCaptureCell(ctx, boundCap));
                return Block(
                    Assign(cellVar, NewArrayBounds(typeof(long), Constant(1))),
                    Assign(ArrayAccess(cellVar, Constant(0)), ParameterValue(bound, ctx)));
            }
            if (!ctx.IsCellBacked(v))
                throw new InvalidOperationException(
                    $"VM compile rejected: captured variable '{v.Name}' has no upvalue cell.");
            return Assign(cellVar, RingCodec.CellArray(ctx.HeapLocal, ctx.VariableReadRaw(v)));
        }
        return Block(
            Assign(cellVar, NewArrayBounds(typeof(long), Constant(1))),
            Assign(ArrayAccess(cellVar, Constant(0)), ReadCaptureSource(cap, ctx)));
    }

    private static void DeclareFreeParameters(Node node, Context ctx, HashSet<Parameter> ownParams) {
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

    private static Expression ReadCaptureSource(Capture cap, Context ctx) {
        if (cap.Variable is not null)
            return ctx.VariableRead(cap.Variable);
        return ParameterValue(cap.Parameter!, ctx);
    }

    /// <summary>Break statement: jump to the named loop, or the innermost if unlabeled.</summary>
    private static Expression EmitBreakStatement(BreakStatement bs, Context ctx) =>
        Goto(ctx.ResolveLoopLabels(bs.Label).breakLabel);

    /// <summary>Continue statement: jump to the named loop's continue, or the innermost if unlabeled.</summary>
    private static Expression EmitContinueStatement(ContinueStatement cs, Context ctx) =>
        Goto(ctx.ResolveLoopLabels(cs.Label).continueLabel);

    /// <summary>
    /// ForLoop: lower to { init; while(condition) { body; increment; } }
    /// When condition is null, treat as 'while(true)'.
    /// </summary>
    private static Expression EmitForLoop(ForLoop fl, Context ctx) {
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

        // Body, increment, and test each start from the loop's entry depth and their
        // slots are dead afterwards; the scope restores it, so the cursor cannot drift.
        Expression bodyExpr;
        using (ctx.OpenRingScope()) {
            bodyExpr = CompileNode(fl.Body, ctx);
        }
        Expression? incrementExpr = null;
        if (fl.Increment is not null) {
            using (ctx.OpenRingScope()) {
                incrementExpr = CompileNode(fl.Increment, ctx);
            }
        }
        Expression test;
        using (ctx.OpenRingScope()) {
            test = CompileConditionAsBool(condition, ctx);
        }

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
        return Block(stmts);
    }

    /// <summary>
    /// ForEachLoop: walk a heap <see cref="System.Collections.IEnumerable"/>,
    /// bind each Current onto the loop variable via <c>BoxToAbi</c>, run the body.
    /// Null / non-enumerable fails loud. Enumerator is disposed when IDisposable.
    /// </summary>
    private static Expression EmitForEachLoop(ForEachLoop fel, Context ctx) {
        // A foreach yields no ring value: the collection slot and the body's slots are
        // dead when it completes, so the whole emission closes one ring scope.
        using var ringScope = ctx.OpenRingScope();
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

        // The method-level scope already closes this region, so the body compiles in it.
        var bodyExpr = CompileNode(fel.Body, ctx);

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
    private static Expression ForEachElementValue(ForEachLoop fel, ParameterExpression enumerator, Context ctx) {
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
            ? RingCodec.ToIeee(Convert(slot, typeof(double)))
            : Convert(RingCodec.FromIeee(slot), typeof(long));
    }

    /// <summary>True for a type that lives in a scalar ring slot (bool or a long-representable
    /// value type, plus IEEE float/double). <c>decimal</c> and reference types do not.</summary>
    private static bool IsScalarSlot(Type t) =>
        t == typeof(double) || t == typeof(float) || t == typeof(bool)
        || (t.IsValueType && RingCodec.IsInline(t));

    /// <summary>Goto statement: jump to a named label.</summary>
    private static Expression EmitGotoStatement(GotoStatement gs, Context ctx) {
        var target = ctx.GetLabel(gs.Target);
        return Goto(target);
    }

    /// <summary>Label declaration: emit a label marker in the expression tree.</summary>
    private static Expression EmitLabelDeclaration(LabelDeclaration ld, Context ctx) {
        var label = ctx.GetLabel(ld.Name);
        return Block(Label(label), CompileNode(ld.Statement, ctx));
    }

    /// <summary>UsingStatement: try/finally Dispose when the resource is IDisposable.</summary>
    private static Expression EmitUsingStatement(UsingStatement us, Context ctx) {
        // A using yields nothing: the resource slot and the body's slots are dead when
        // it completes, so the whole emission closes one ring scope.
        using var ringScope = ctx.OpenRingScope();
        var resourceExpr = CompileNode(us.Resource, ctx);
        int resSlot = ctx.RingDepth - 1;
        var bodyExpr = CompileNode(us.Body, ctx);
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

    private static Type ResolveCatchClrType(Node? exceptionType, Context ctx) {
        if (exceptionType is null) return typeof(Exception);
        var resolved = ctx.TryClrType(exceptionType);
        return resolved is not null && typeof(Exception).IsAssignableFrom(resolved)
            ? resolved
            : typeof(Exception);
    }

    /// <summary>Return statement: write value to frame slot, set SP, jump to exit.
    /// A null <see cref="Return.Value"/> is a void return (no slot write).</summary>
    private static Expression EmitReturn(Return ret, Context ctx) {
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