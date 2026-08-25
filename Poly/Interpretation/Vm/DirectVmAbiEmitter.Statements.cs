using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

public static partial class DirectVmAbiEmitter {

    private static Expression EmitAssignment(Assignment a, AbiCtx ctx) {
        if (a.Destination is IndexAccess indexAccess) {
            if (indexAccess.Value is Variable arrVarW
                && ctx.TryGetFrameLocalBase(arrVarW) is int flBaseW) {
                Expression idxVal = indexAccess.Arguments.Length > 0
                    ? CompileValue(indexAccess.Arguments[0], ctx)
                    : Constant(0L);
                Expression valVal = CompileValue(a.Value, ctx);
                int flResultSlot = ctx.AllocSlot();
                ctx.RingDepth = flResultSlot + 1;
                var idxTmp = Variable(typeof(int), "_flIdx");
                return Block([idxTmp],
                    Assign(idxTmp, Convert(idxVal, typeof(int))),
                    Assign(ctx.RingVar(flResultSlot), valVal),
                    Assign(ArrayAccess(ctx.SlotsLocal, Add(Constant(flBaseW), idxTmp)),
                        ctx.RingVar(flResultSlot)),
                    ctx.RingVar(flResultSlot));
            }

            var arrExpr = CompileNode(indexAccess.Value, ctx);
            int arrSlot = ctx.RingDepth - 1;
            var idxExpr = CompileNode(
                indexAccess.Arguments.Length > 0 ? indexAccess.Arguments[0] : new Constant(0), ctx);
            int idxSlot = ctx.RingDepth - 1;
            var valExpr = CompileNode(a.Value, ctx);
            int valSlot = ctx.RingDepth - 1;

            var rawObj = Convert(
                ArrayAccess(ctx.HeapRawSlots, Convert(ctx.RingVar(arrSlot), typeof(int))),
                typeof(object));
            var idx = Convert(ctx.RingVar(idxSlot), typeof(int));
            var val = ctx.RingVar(valSlot);

            Type? elemType = ctx.Analysis?.GetResolvedType(indexAccess) is ClrTypeDefinition clrElem
                ? clrElem.RuntimeType
                : null;

            Expression store;
            if (elemType is { IsValueType: true }) {
                var longArr = Variable(typeof(long[]), "_assignLongArr");
                store = Block([longArr],
                    Assign(longArr, Convert(rawObj, typeof(long[]))),
                    Assign(ArrayAccess(longArr, idx), val));
            }
            else if (elemType is not null) {
                var objArr = Variable(typeof(object[]), "_assignObjArr");
                store = Block([objArr],
                    Assign(objArr, Convert(rawObj, typeof(object[]))),
                    Assign(ArrayAccess(objArr, idx), Convert(val, typeof(object))));
            }
            else {
                var longArr = Variable(typeof(long[]), "_assignLongArr");
                var objArr = Variable(typeof(object[]), "_assignObjArr");
                store = IfThenElse(
                    TypeIs(rawObj, typeof(long[])),
                    Block([longArr],
                        Assign(longArr, Convert(rawObj, typeof(long[]))),
                        Assign(ArrayAccess(longArr, idx), val)),
                    Block([objArr],
                        Assign(objArr, Convert(rawObj, typeof(object[]))),
                        Assign(ArrayAccess(objArr, idx), Convert(val, typeof(object)))));
            }

            ctx.RingDepth = arrSlot + 1;
            return Block(arrExpr, idxExpr, valExpr, store,
                Assign(ctx.RingVar(arrSlot), val));
        }

        if (a.Destination is Member member) {
            var resolved = ctx.Analysis?.GetResolvedMember(member);
            if (resolved is null)
                throw new NotSupportedException(
                    $"Assignment to unresolved member '{member.MemberName}'");

            int d = ctx.RingDepth;
            var instanceExpr = CompileNode(member.Value, ctx);
            int instanceSlot = ctx.RingDepth - 1;
            var valueExpr = CompileNode(a.Value, ctx);
            int valueSlot = ctx.RingDepth - 1;

            Expression instanceObj;
            var declaringTypeDef = resolved.DeclaringTypeDefinition;
            bool isValueType = declaringTypeDef is ClrTypeDefinition clrDef
                && clrDef.RuntimeType.IsValueType;
            if (isValueType)
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
                && AbiValueTypes.IsLongRepresentable(clrMemberType)) {
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
            return Block(instanceExpr, valueExpr,
                writeExpr ?? throw new NotSupportedException(
                    $"Member '{member.MemberName}' does not support EmitWrite"),
                Assign(ctx.RingVar(outSlot), ctx.RingVar(valueSlot)));
        }

        if (a.Destination is not Variable destVar) {
            throw new NotSupportedException(
                $"Assignment destination must be a Variable or IndexAccess, got {a.Destination.GetType().Name}");
        }

        if (a.Value is NewArray newArr && newArr.Length is Constant lenConst
            && TryValueToLong(lenConst.Value, out long arrLen)
            && arrLen > 0 && arrLen <= AbiCtx.SmallArrayThreshold) {
            Type elemType = newArr.ElementType switch {
                ClrTypeReference ctr when ctr.RuntimeType.IsValueType => ctr.RuntimeType,
                _ => typeof(object)
            };
            if (elemType.IsValueType) {
                int baseOffset = ctx.AllocateSmallArray();
                ctx.TrackFrameLocalArray(destVar, baseOffset);
                var zeroInits = new Expression[(int)arrLen];
                for (int zi = 0; zi < (int)arrLen; zi++)
                    zeroInits[zi] = Assign(
                        ArrayAccess(ctx.SlotsLocal, Constant(baseOffset + zi)),
                        Constant(0L));
                int slot = ctx.AllocSlot();
                return Block(
                    Block(zeroInits),
                    ctx.VariableWrite(destVar, Constant((long)baseOffset)),
                    Assign(ctx.RingVar(slot), Constant((long)baseOffset)));
            }
        }

        ctx.UntrackFrameLocalArray(destVar);

        if (ctx.TryGetCapture(destVar, out int capIndex)) {
            int d = ctx.RingDepth;
            var valueExpr = CompileNode(a.Value, ctx);
            int valSlot = ctx.RingDepth - 1;
            var foldVal = FoldResultToSlot(ref valSlot, d, ctx);
            var closureArr = Convert(
                ArrayAccess(ctx.HeapRawSlots, Convert(ctx.ClosureHandle, typeof(int))),
                typeof(object[]));
            var store = Assign(
                ArrayAccess(closureArr, Constant(capIndex + 1)),
                Convert(ctx.RingVar(valSlot), typeof(object)));
            ctx.RingDepth = valSlot + 1;
            return Block(valueExpr, foldVal, store);
        }

        int d2 = ctx.RingDepth;
        var valueExpr2 = CompileNode(a.Value, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var result = ctx.VariableWrite(destVar, ctx.RingVar(resultSlot));
        ctx.RingDepth = resultSlot + 1;
        return Block(valueExpr2, result);
    }

    private static Expression EmitBlock(Block block, AbiCtx ctx) {
        ctx.PushScope();
        var varInitExprs = new List<Expression>();
        foreach (var v in block.Variables) {
            if (v is Variable variable) {
                ctx.DeclareVariable(variable);
                varInitExprs.Add(Assign(ctx.VariableRead(variable), Constant(0L)));
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
        int d = ctx.RingDepth;
        var valueExpr = CompileNode(sw.Value, ctx);
        int valSlot = ctx.RingDepth - 1;
        var foldVal = FoldResultToSlot(ref valSlot, d, ctx);
        int savedDepth = valSlot + 1;
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
        return Block(valueExpr, foldVal, rest, ctx.RingVar(outSlot));
    }

    private static Expression EmitWhileLoop(WhileLoop wl, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var breakLabel = Label("wl_break");
        var continueLabel = Label("wl_continue");
        ctx.PushLoopScope(breakLabel, continueLabel);
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
        ctx.PushLoopScope(breakLabel, continueLabel);
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
        if (ts.Exception is New) {
            int d = ctx.RingDepth;
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
        var sideEffects = CompileNode(ts.Exception, ctx);
        return Block(sideEffects, Throw(New(typeof(Exception))));
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
        var saveResumeId = Assign(ctx.ProgramCounter, Constant(step));
        var saveFramePos = Assign(
            Property(ctx.State, nameof(VmState.FramePos)),
            ctx.FramePosLocal);
        return Block(
            Label(resumeLabel),
            innerExpr,
            setCurrentNode,
            setStatus,
            saveResumeId,
            saveFramePos,
            Goto(ctx.ExitLabel));
    }

    private static Expression EmitParameter(Parameter p, AbiCtx ctx) {
        if (!ctx.TryGetParameterSlot(p, out int paramIdx)) {
            paramIdx = ctx.DeclareParameter(p);
        }
        int slot = ctx.AllocSlot();
        // Root SetArgs({ this }) lives in InstanceHandle so locals cannot wipe it.
        // Nested lambda parameters use the frame / inline map.
        Expression value = paramIdx == 0 && ctx.ParamSlotOffset == 0 && !ctx.HasInlineParameters
            ? ctx.InstanceHandle
            : ctx.ParameterRead(paramIdx);
        return Assign(ctx.RingVar(slot), value);
    }

    /// <summary>
    /// Domain programs bind the instance via <c>SetArgs({ this })</c> at slot 0.
    /// After SetArgs, ThisReference is that handle. Unset slot 0 is ABI null 0.
    /// </summary>
    private static Expression EmitThis(AbiCtx ctx) {
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot), ctx.InstanceHandle);
    }

    private static Expression EmitLambda(Lambda lambda, AbiCtx ctx) {
        if (lambda.LambdaIndex < 0)
            throw new InvalidOperationException("Lambda.LambdaIndex not set during lambda collection");
        var captures = FindCaptures(lambda.Body, ctx);
        for (int i = 0; i < captures.Count; i++)
            ctx.DeclareCapture(captures[i].Variable, i);
        int arrLen = 1 + captures.Count;
        var closureArrVar = Variable(typeof(object[]), "_closureArr");
        var body = new List<Expression>();
        body.Add(Assign(closureArrVar, NewArrayBounds(typeof(object), Constant(arrLen))));
        body.Add(Assign(ArrayAccess(closureArrVar, Constant(0)),
            Convert(Constant((long)lambda.LambdaIndex), typeof(object))));
        for (int i = 0; i < captures.Count; i++) {
            var cap = captures[i];
            body.Add(Assign(ArrayAccess(closureArrVar, Constant(1 + i)),
                Convert(ctx.VariableRead(cap.Variable), typeof(object))));
        }
        var handle = Call(ctx.HeapLocal, HeapAllocate,
            Convert(closureArrVar, typeof(object)));
        int slot = ctx.AllocSlot();
        body.Add(Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));
        return Block([closureArrVar], body);
    }

    private static List<Capture> FindCaptures(Node body, AbiCtx outerCtx) {
        var result = new List<Capture>();
        var seen = new HashSet<Variable>(ReferenceEqualityComparer.Instance);
        FindCapturesRecursive(body, outerCtx, result, seen);
        return result;
    }

    private static void FindCapturesRecursive(
        Node node, AbiCtx outerCtx, List<Capture> result, HashSet<Variable> seen) {
        if (node is Variable v && seen.Add(v)) {
            if (outerCtx.TryGetVariable(v, out int slotIndex)) {
                result.Add(new Capture(v, slotIndex));
            }
            return;
        }
        foreach (var child in node.Children) {
            if (child is not null)
                FindCapturesRecursive(child, outerCtx, result, seen);
        }
    }

    /// <summary>Break statement: jump to current loop's break label.</summary>
    private static Expression EmitBreakStatement(BreakStatement bs, AbiCtx ctx) {
        var labels = ctx.CurrentLoopLabels;
        if (labels == null)
            throw new InvalidOperationException("Break outside loop");
        return Goto(labels.Value.breakLabel);
    }

    /// <summary>Continue statement: jump to current loop's continue label.</summary>
    private static Expression EmitContinueStatement(ContinueStatement cs, AbiCtx ctx) {
        var labels = ctx.CurrentLoopLabels;
        if (labels == null)
            throw new InvalidOperationException("Continue outside loop");
        return Goto(labels.Value.continueLabel);
    }

    /// <summary>
    /// ForLoop: lower to { init; while(condition) { body; increment; } }
    /// When condition is null, treat as 'while(true)'.
    /// </summary>
    private static Expression EmitForLoop(ForLoop fl, AbiCtx ctx) {
        var breakLabel = Label("for_break");
        var continueLabel = Label("for_continue");
        ctx.PushLoopScope(breakLabel, continueLabel);

        var stmts = new List<Expression>();
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
        ctx.PushLoopScope(breakLabel, continueLabel);
        ctx.PushScope();
        ctx.DeclareVariable(fel.LoopVariable);

        int d = ctx.RingDepth;
        var collectionExpr = CompileNode(fel.Collection, ctx);
        int colSlot = ctx.RingDepth - 1;
        var foldCol = FoldResultToSlot(ref colSlot, d, ctx);

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
                ctx.VariableWrite(fel.LoopVariable,
                    Call(null, BoxToAbiInfo, ctx.HeapLocal, Property(enumerator, EnumeratorCurrent))),
                bodyExpr,
                Label(continueLabel)),
            breakLabel);

        ctx.PopLoopScope();
        ctx.PopScope();
        ctx.RingDepth = d;

        return Block(
            [enumerator, colObj],
            collectionExpr,
            foldCol,
            Assign(colObj, Call(ctx.HeapLocal, HeapUnsafeGet,
                Convert(ctx.RingVar(colSlot), typeof(int)))),
            Assign(enumerator, Call(null, GetEnumeratorOrThrowInfo, colObj)),
            TryFinally(
                loop,
                IfThen(
                    TypeIs(enumerator, typeof(IDisposable)),
                    Call(Convert(enumerator, typeof(IDisposable)), IDisposableDispose))));
    }

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
        if (ret.Value is null)
            return Goto(ctx.ExitLabel);

        int d = ctx.RingDepth;
        var valueExpr = CompileNode(ret.Value, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);

        return Block(
            valueExpr, fold,
            Assign(ArrayAccess(ctx.SlotsLocal, ctx.FramePosLocal), ctx.RingVar(resultSlot)),
            Assign(ctx.SlotsStackPointer,
                Add(ctx.FramePosLocal, Constant(1))),
            Goto(ctx.ExitLabel));
    }
}