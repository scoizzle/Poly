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
            // A promoted array's element store is the CLR local indexed, mirroring the read.
            ParameterExpression? cachedArray = null;
            bool cachedStore = indexAccess.Value is Variable cached
                && ctx.TryArrayCache(cached, out cachedArray);
            Value? arrSlot = cachedStore ? null : ring.AddValue(CompileNode(indexAccess.Value, ctx));
            var idxSlot = ring.AddValue(CompileNode(
                indexAccess.Arguments.Length > 0 ? indexAccess.Arguments[0] : new Constant(0), ctx));
            var valSlot = ring.AddValue(CompileNode(a.Value, ctx));

            Type? elemType = ctx.Analysis?.GetResolvedType(indexAccess) is ClrTypeDefinition clrElem
                ? clrElem.RuntimeType
                : null;
            bool valueIsIeee = RingCodec.IsIeee(a.Value, ctx.Analysis);
            Expression store;
            if (cachedStore)
                store = RingCodec.CachedElementWrite(
                    cachedArray!, idxSlot.Read, valSlot.Read, elemType, valueIsIeee);
            else
                store = RingCodec.ArrayElementWrite(
                    ctx.HeapLocal, arrSlot!.Value.Read, idxSlot.Read, valSlot.Read,
                    elemType, valueIsIeee);

            var storeSlot = cachedStore ? idxSlot : arrSlot!.Value;
            ctx.RingDepth = storeSlot.RingSlot + 1;
            return ring.Then(Block(store, Assign(storeSlot.Read, valSlot.Read)));
        }

        if (a.Destination is Member member) {
            var resolved = ctx.Analysis?.GetResolvedMember(member);
            if (resolved is null)
                throw new NotSupportedException(
                    $"Assignment to unresolved member '{member.MemberName}'");

            var ring = ctx.OpenRing();
            var instanceSlot = ring.AddValue(CompileNode(member.Value, ctx));
            var valueSlot = ring.AddValue(CompileNode(a.Value, ctx));

            Expression instanceObj;
            var declaringTypeDef = resolved.DeclaringTypeDefinition;
            var inlineClr = declaringTypeDef is ClrTypeDefinition clrDef
                && clrDef.RuntimeType.IsValueType
                && RingCodec.IsInline(clrDef.RuntimeType)
                ? clrDef.RuntimeType
                : null;
            if (inlineClr is not null)
                instanceObj = LocalType.Of(inlineClr) is LocalType.TemporalLocal { IsWord: true }
                    ? Convert(
                        LocalType.Decode(instanceSlot.Read, inlineClr, inlineClr, ctx.HeapLocal),
                        typeof(object))
                    : Convert(instanceSlot.Read, typeof(object));
            else
                instanceObj = Call(ctx.HeapLocal,
                    HeapUnsafeGet,
                    Convert(instanceSlot.Read, typeof(int)));

            var memberTypeDef = resolved.MemberTypeDefinition;
            var clrMemberType = memberTypeDef.GetRuntimeType()
                ?? memberTypeDef.PrimitiveType?.GetClrType();
            Expression writeValue;
            if (clrMemberType is not null && Nullable.GetUnderlyingType(clrMemberType) is { } memberUnderlying) {
                writeValue = NullableMemberWrite(a.Value, valueSlot.RingSlot, clrMemberType, memberUnderlying, ctx);
            }
            else if (clrMemberType is not null
                && clrMemberType.IsValueType
                && RingCodec.IsInline(clrMemberType)) {
                if (clrMemberType == typeof(bool))
                    writeValue = Condition(Equal(valueSlot.Read, Constant(0L)),
                        Constant(false, typeof(object)),
                        Constant(true, typeof(object)));
                else if (LocalType.Of(clrMemberType) is LocalType.TemporalLocal { IsWord: true })
                    writeValue = Convert(
                        LocalType.Decode(valueSlot.Read, clrMemberType, clrMemberType, ctx.HeapLocal),
                        typeof(object));
                else
                    writeValue = Convert(Convert(valueSlot.Read, clrMemberType), typeof(object));
            }
            else {
                writeValue = Call(ctx.HeapLocal,
                    HeapUnsafeGet,
                    Convert(valueSlot.Read, typeof(int)));
            }

            var writeExpr = resolved.EmitWrite(instanceObj, writeValue);
            var outSlot = ctx.Reserve();
            ctx.RingDepth = outSlot.RingSlot + 1;
            return ring.Then(Block(
                writeExpr ?? throw new NotSupportedException(
                    $"Member '{member.MemberName}' does not support EmitWrite"),
                Assign(outSlot.Read, valueSlot.Read)));
        }

        if (a.Destination is not Variable destVar) {
            throw new InvalidOperationException(
                $"VM compile rejected: assignment destination must be a Variable, Member, or IndexAccess, got {a.Destination.GetType().Name}.");
        }

        var destination = ctx.Resolve(destVar)
            ?? throw new InvalidOperationException(
                $"VM compile rejected: assignment destination '{destVar.Name}' is not declared in any scope.");
        var value = ctx.OpenRing();
        var compiled = CacheDefinition(a, destVar, ctx) ?? CompileNode(a.Value, ctx);
        int valueIndex = ctx.RingDepth - 1;
        value.Add(compiled);
        var result = destination.Write(ctx, AssignedValue(ctx, destVar, a.Value, valueIndex, compiled));
        return value.Then(result);
    }

    /// <summary>A promoted array's definition: the array is created once and bound to both
    /// the typed local and the variable's slot, which keeps the handle. This is the one
    /// site that supplies <see cref="EmitNewArray(NewArray, Context, ParameterExpression?)"/>
    /// with a cache, so promotion and its allocation cannot drift apart.</summary>
    private static Expression? CacheDefinition(Assignment a, Variable destVar, Context ctx) =>
        a.Value is NewArray newArray
        && !IsReplaced(newArray, ctx)
        && ctx.TryArrayCache(destVar, out var cache)
            ? EmitNewArray(newArray, ctx, cache)
            : null;

    /// <summary>True when analysis replaced <paramref name="node"/> with another node, which
    /// is what <see cref="Compile"/> follows. A replaced definition is not the array it
    /// names, so it keeps the general path.</summary>
    private static bool IsReplaced(Node node, Context ctx) =>
        ctx.Analysis?.GetNodeReplacement(node) is { } replacement && replacement != node;

    /// <summary>
    /// The compiled value at the variable's own representation. The emitter's own type
    /// says how the value was <em>encoded</em> (<c>double</c>, <c>float</c>, or the word),
    /// while the value's analysis type says what it <em>means</em>: a word encoding of a
    /// double is its bit pattern, an integer word is the number. The numeric conversion
    /// between the two width classes happens once here — an integer assigned to a
    /// <c>double</c> variable widens, a <c>double</c> assigned to an integer truncates —
    /// and the storage then takes the value as the word if the storage is a word (a
    /// cell-backed local, whose register holds the cell handle).
    /// </summary>
    private static Expression AssignedValue(
        Context ctx, Variable destination, Node value, int valueIndex, Expression compiled) {
        var emitted = ctx.RingVar(valueIndex, RingCodec.SlotTypeOf(compiled.Type));
        var destType = ctx.SlotTypeOf(destination);
        // A bool? local cannot be assigned the 0/1 long a comparison leaves.
        // Convert(long, bool) does not exist; the nullable operand does the 0/1 test.
        if (Nullable.GetUnderlyingType(destType) is not null)
            return AsNullableOperand(value, emitted, destType, ctx);
        var valueType = ctx.SlotTypeOf(value);
        var decoded = valueType == emitted.Type ? emitted : RingCodec.FromWord(emitted, valueType);
        return RingCodec.NumericTo(decoded, destType);
    }

    private static Expression EmitBlock(Block block, Context ctx) {
        ctx.PushScope();
        var varInitExprs = new List<Expression>();
        var releases = new List<Expression>();
        var cacheClears = new List<Expression>();
        foreach (var v in block.Variables) {
            if (v is Variable variable) {
                ctx.DeclareVariable(variable);
                varInitExprs.Add(ctx.NeedsCell(variable)
                    ? EmitUpvalueCellInit(variable, ctx)
                    : ctx.VariableWrite(variable, ctx.VariableZero(variable)));
                if (ArrayCacheCandidate(variable, block, ctx) is { } cache) {
                    // The promotion predicate is the only proof of death this VM has: the
                    // array is not returned, passed, aliased, or captured, and every other
                    // reference is an IndexAccess receiver. A definition that is itself the
                    // block's value is the exception the predicate cannot see — that value
                    // is the handle — so such a handle stays live.
                    if (!IsBlockValue(block, variable)) {
                        releases.Add(Call(ctx.HeapLocal, HeapRecycle,
                            Convert(ctx.VariableReadRaw(variable), typeof(int))));
                    }
                    cacheClears.Add(Assign(cache, Constant(null, cache.Type)));
                }
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
        Type? valueClrType = null;
        Context.StatementScope? open = null;
        for (int i = 0; i < block.Nodes.Count; i++) {
            var statement = block.Nodes[i];
            // A comment or a label emits no code and no value, so it must not release
            // what the previous statement left: that value is still the block's result
            // (`[return x, comment]` yields x).
            bool resetsDepth = statement is not (Comment or LabelDeclaration);
            if (resetsDepth) {
                open?.Dispose();
                open = ctx.OpenStatement(entryDepth);
            }
            // The suspend spill reads this set: a variable lease is stored only when
            // a later statement (or the rest of this one) names the binding. A loop
            // or a goto makes the continuation unknowable, so it keeps the
            // conservative all-leases set.
            var readsAfter = ReadsAfterFrom(block.Nodes, i, ctx);
            ctx.PushReadsAfter(readsAfter);
            Expression code;
            try {
                code = CompileStatement(statement, ctx);
            }
            finally {
                ctx.PopReadsAfter();
            }
            stmtExprs.Add(code);
            if (statement is not Comment)
                valueClrType = code.Type;
        }
        if (open is not null) {
            open.Hold();
            open.Dispose();
        }
        var stores = ctx.EmitScopeStores();
        ctx.PopScope();
        var observation = WhenDebugHookAttached(ctx, stores);
        var all = new List<Expression>(
            varInitExprs.Count + stmtExprs.Count + (observation is null ? 0 : 1) + releases.Count + cacheClears.Count);
        all.AddRange(varInitExprs);
        all.AddRange(stmtExprs);
        // The snapshot is what the hook's span and a later GetLocals read. It runs
        // only while DebugHook is attached. NoDebug has no hook property, so it emits none.
        if (observation is not null)
            all.Add(observation);
        // A promoted array's cache local is keyed by slot, and a slot is handed to the
        // next sibling block's variable, so it is cleared once its own scope is over: a
        // later block that read it before defining its own array then fails closed on
        // null instead of reading this block's array. The handle is released just before
        // the clear — the promotion predicate proved no one can observe the array again,
        // so the free is what makes the null mean what it looks like.
        all.AddRange(releases);
        all.AddRange(cacheClears);
        if (all.Count == 0) return Empty();
        // A trailing snapshot store, recycle, or cache clear would otherwise type the block
        // as what it does. Re-read the live value slot after them so the block's own type is
        // the value's — an IEEE value stays a double instead of being read as bits, and a
        // word value stays a word instead of the array a cache clear holds.
        var valueSlotType = RingCodec.SlotTypeOf(valueClrType);
        // The hook guard is void, so a block that appends one has to re-read its value
        // or the block's type collapses to void and the ring result is read at the wrong width.
        bool trailing = observation is not null || releases.Count > 0 || cacheClears.Count > 0;
        if (trailing && ctx.RingDepth > entryDepth
            && (observation is not null || RingCodec.IsTypedSlot(valueSlotType) || cacheClears.Count > 0))
            all.Add(ctx.RingVar(ctx.RingDepth - 1, valueSlotType));
        return Block(all);
    }

    /// <summary>The bindings read at or after statement <paramref name="start"/>.
    /// Null when the continuation is not straight-line: a loop, a goto, or a label
    /// can re-enter a read the suffix does not contain, so the suspend spill keeps
    /// the conservative set.</summary>
    private static HashSet<Variable>? ReadsAfterFrom(IReadOnlyList<Node> nodes, int start, Context ctx) {
        HashSet<Variable>? inherited =
            ctx.HasReadsAfterScope ? ctx.ReadsAfterSuspend : new HashSet<Variable>();
        if (inherited is null)
            return null;
        var reads = new HashSet<Variable>(inherited, ReferenceEqualityComparer.Instance);
        for (int i = start; i < nodes.Count; i++) {
            if (ContainsLoopOrGoto(nodes[i]))
                return null;
            CollectReads(nodes[i], ctx, reads);
        }
        return reads;
    }

    private static void CollectReads(Node node, Context ctx, HashSet<Variable> into) {
        switch (node) {
            case Block block:
                foreach (var child in block.Nodes)
                    CollectReads(child, ctx, into);
                return;
            case Variable variable:
                into.Add(ctx.DeclaredVariable(variable) ?? variable);
                return;
            case Poly.Ast.Nodes.Parameter:
                return;
            default:
                foreach (var child in node.Children)
                    if (child is not null)
                        CollectReads(child, ctx, into);
                return;
        }
    }

    private static bool ContainsLoopOrGoto(Node node) => node switch {
        WhileLoop or DoWhileLoop or ForLoop or ForEachLoop
            or GotoStatement or LabelDeclaration => true,
        _ => node.Children.Any(child => child is not null && ContainsLoopOrGoto(child))
    };

    /// <summary>True when the last statement that produces the block's value is the
    /// variable's definition, so that value <em>is</em> the array's handle: the enclosing
    /// expression still observes it, and recycling it there would dangle a live handle.
    /// A comment produces no value, so it does not end the search.</summary>
    private static bool IsBlockValue(Block block, Variable variable) {
        for (int i = block.Nodes.Count - 1; i >= 0; i--) {
            if (block.Nodes[i] is Comment)
                continue;
            return block.Nodes[i] is Assignment assignment && Binds(assignment.Destination, variable);
        }
        return false;
    }

    /// <summary>The typed CLR local that caches <paramref name="variable"/>'s array, when
    /// every reference the declaring block makes to it is an element access. Anything else
    /// — a reassignment, <c>.Length</c>, an argument, a return, a capture, a use inside a
    /// nested function body, or an element type the type pass cannot resolve — keeps the
    /// heap path: the local is a cache for element access, never a replacement for the
    /// variable.
    /// <para>The definition must also be reachable before every use, so the local is
    /// always assigned by the time it is read.</para></summary>
    private static ParameterExpression? ArrayCacheCandidate(Variable variable, Block block, Context ctx) {
        if (ctx.NeedsCell(variable))
            return null;
        int definition = -1;
        NewArray? created = null;
        for (int i = 0; i < block.Nodes.Count; i++) {
            if (block.Nodes[i] is not Assignment assignment || !Binds(assignment.Destination, variable))
                continue;
            // A second assignment anywhere in the block — including inside a nested one —
            // would leave the local disagreeing with the slot after the rebind.
            if (definition >= 0 || assignment.Value is not NewArray newArray || IsReplaced(newArray, ctx))
                return null;
            definition = i;
            created = newArray;
        }
        if (created is not null && ArrayElementClrType(created.ElementType, ctx) is { } elementClr) {
            for (int i = 0; i < definition; i++) {
                if (ReferencesName(block.Nodes[i], variable))
                    return null;
            }
            if (created.Length is { } length && ReferencesName(length, variable))
                return null;
            for (int i = definition + 1; i < block.Nodes.Count; i++) {
                if (!OnlyElementAccesses(block.Nodes[i], variable, block))
                    return null;
            }
            return ctx.PromoteArray(variable, RingCodec.ArrayClrType(elementClr));
        }
        return null;
    }

    /// <summary>Every reference to the binding in a subtree must be the receiver of an
    /// <see cref="IndexAccess"/>. A nested function body is a separate delegate and cannot
    /// see the local at all, so a reference inside one disqualifies the variable; a nested
    /// block that redeclares the name makes the reference ambiguous, which also disqualifies
    /// it rather than guessing which binding a name match meant.</summary>
    private static bool OnlyElementAccesses(
        Node? node, Variable variable, Block declaring, bool receiver = false) {
        if (node is null)
            return true;
        if (Binds(node, variable))
            return receiver;
        if (node is Lambda)
            return !ReferencesName(node, variable);
        if (node is Block nested && !ReferenceEquals(nested, declaring)
            && nested.Variables.Any(v => v is Variable other
                && !ReferenceEquals(other, variable)
                && other.Name == variable.Name))
            return false;
        if (node is ForLoop { DeclaredVariables: { } declared }
            && declared.Any(other => !ReferenceEquals(other, variable) && other.Name == variable.Name))
            return false;
        if (node is IndexAccess access)
            return OnlyElementAccesses(access.Value, variable, declaring, receiver: true)
                && access.Arguments.All(argument => OnlyElementAccesses(argument, variable, declaring));
        // A StridedSetBits receiver writes elements of the same buffer, so it is the same kind of
        // use: the array is never handed on, only indexed. Without this the sieve's mark loop
        // reached the array as an argument and paid two heap derefs per iteration.
        if (node is StridedSetBits strided)
            return OnlyElementAccesses(strided.Array, variable, declaring, receiver: true)
                && OnlyElementAccesses(strided.StartValue, variable, declaring)
                && OnlyElementAccesses(strided.Step, variable, declaring)
                && OnlyElementAccesses(strided.Limit, variable, declaring);
        return node.Children.All(child => OnlyElementAccesses(child, variable, declaring));
    }

    /// <summary>True when any node in the subtree resolves to the binding — by identity, or
    /// by name, because a use site reaches a declaration it does not share an instance with.
    /// </summary>
    private static bool ReferencesName(Node? node, Variable variable) {
        if (node is null)
            return false;
        if (Binds(node, variable))
            return true;
        return node.Children.Any(child => ReferencesName(child, variable));
    }

    /// <summary>True when a reference node denotes the binding: the declaration's own
    /// instance, or a use of the same name. Shadowing is excluded by the callers, which
    /// bail on a nested declaration of the name.</summary>
    private static bool Binds(Node? node, Variable variable) =>
        node is Variable use
        && (ReferenceEquals(use, variable)
            || string.Equals(use.Name, variable.Name, StringComparison.Ordinal));

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
        var val = ring.AddValue(CompileNode(sw.Value, ctx));
        int savedDepth = val.RingSlot + 1;

        var outValue = ctx.Reserve();
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
                Assign(outValue.Read,
                    RingCodec.ToWord(def.RequireResult(sw.DefaultCase).Read(ctx))));
        }
        else {
            rest = Assign(outValue.Read, Constant(0L));
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
                    Equal(val.Read,
                        RingCodec.ToWord(pattern.RequireResult(c.Pattern).Read(ctx))),
                    Block(body.Code,
                        Assign(outValue.Read,
                            RingCodec.ToWord(body.RequireResult(c.Body).Read(ctx)))),
                    rest));
        }

        ctx.RingDepth = outValue.RingSlot + 1;
        return ring.Then(Block(rest, outValue.Read));
    }

    /// <summary>Where a loop's test runs: at the top of each iteration, or after its
    /// body — the difference that makes <c>do</c> run at least once.</summary>
    private enum LoopTestPosition { BeforeBody, AfterBody }

    /// <summary>
    /// What one loop means, as opposed to how any loop is built: its test, the
    /// statements that run once per iteration around the body, the body itself, and
    /// what <c>continue</c> lands on. <see cref="EmitLoopScaffold"/> supplies the rest.
    /// </summary>
    private sealed record LoopPlan(
        string LabelPrefix,
        string? Name,
        LoopTestPosition TestPosition,
        Func<Context, Expression> Test,
        Func<Context, Expression>? PerIteration,
        Func<Context, Expression> Body,
        Func<Context, Expression>? AfterContinue);

    /// <summary>
    /// The one loop shape all four loop emitters share: break/continue label pair, the
    /// loop scope <c>break</c>/<c>continue</c> resolve in, the iteration guard, the
    /// test's jump to the break label, the continue label, and the ring rewind around
    /// each compile. Four hand-kept copies of this order is how #259 happened, so the
    /// order lives here once and the emitters only describe their plan.
    /// </summary>
    private static Expression EmitLoopScaffold(LoopPlan plan, Context ctx) {
        var breakLabel = Label($"{plan.LabelPrefix}_break");
        var continueLabel = Label($"{plan.LabelPrefix}_continue");
        ctx.PushLoopScope(breakLabel, continueLabel, plan.Name);

        // Test, per-iteration statements, body and after-continue each start from the
        // loop's entry depth and their slots are dead afterwards; the scope restores it,
        // so the cursor cannot drift.
        Expression test;
        Expression body;
        if (plan.TestPosition == LoopTestPosition.BeforeBody) {
            using (ctx.OpenRingScope()) {
                test = plan.Test(ctx);
            }
            using (ctx.OpenRingScope()) {
                body = plan.Body(ctx);
            }
        }
        else {
            using (ctx.OpenRingScope()) {
                body = plan.Body(ctx);
            }
            using (ctx.OpenRingScope()) {
                test = plan.Test(ctx);
            }
        }
        Expression? perIteration = null;
        if (plan.PerIteration is not null) {
            using (ctx.OpenRingScope()) {
                perIteration = plan.PerIteration(ctx);
            }
        }
        Expression? afterContinue = null;
        if (plan.AfterContinue is not null) {
            using (ctx.OpenRingScope()) {
                afterContinue = plan.AfterContinue(ctx);
            }
        }

        var loopBody = new List<Expression> { EmitLoopIterationGuard(ctx) };
        if (plan.TestPosition == LoopTestPosition.BeforeBody)
            loopBody.Add(IfThen(Not(test), Goto(breakLabel)));
        if (perIteration is not null)
            loopBody.Add(perIteration);
        loopBody.Add(body);
        loopBody.Add(Label(continueLabel));
        if (afterContinue is not null)
            loopBody.Add(afterContinue);
        if (plan.TestPosition == LoopTestPosition.AfterBody)
            loopBody.Add(IfThen(Not(test), Goto(breakLabel)));

        var result = Loop(Block(loopBody), breakLabel);
        ctx.PopLoopScope();
        return result;
    }

    private static Expression EmitWhileLoop(WhileLoop wl, Context ctx) =>
        EmitLoopScaffold(
            new LoopPlan(
                "wl",
                wl.Label,
                LoopTestPosition.BeforeBody,
                Test: c => CompileConditionAsBool(wl.Condition, c),
                PerIteration: null,
                Body: c => CompileNode(wl.Body, c),
                AfterContinue: null),
            ctx);

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

    private static Expression EmitDoWhileLoop(DoWhileLoop dwl, Context ctx) =>
        EmitLoopScaffold(
            new LoopPlan(
                "dwl",
                dwl.Label,
                LoopTestPosition.AfterBody,
                Test: c => CompileConditionAsBool(dwl.Condition, c),
                PerIteration: null,
                Body: c => CompileNode(dwl.Body, c),
                AfterContinue: null),
            ctx);

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
        ctx.EnterUnresumable();
        try {
            var tryBody = CompileNode(tcf.TryBlock, ctx).Code;
            Expression? finallyBody = null;
            if (tcf.FinallyBlock != null) {
                finallyBody = CompileNode(tcf.FinallyBlock, ctx).Code;
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
                    // The bind pass already built the declaration the body uses point at.
                    // A second Variable of the same name would not be that binding.
                    synthetic = ctx.Analysis?.GetMetadata<CatchBindingMetadata>(clause.Body)?.Declaration
                        ?? new Variable(clause.VariableName);
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
                var catchObservation = WhenDebugHookAttached(ctx, catchStores);
                if (catchObservation is not null)
                    catchBodyExpr = Block(catchBodyExpr, catchObservation);
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
        finally {
            ctx.LeaveUnresumable();
        }
    }

    /// <summary>Lease-to-frame copies exist for the debugger. They run only inside
    /// <c>if (state.DebugHook != null)</c>. <see cref="CompilationMode.NoDebug"/> never
    /// sets the hook property, so it emits no copy and no check. A frame-only binding
    /// is absent from <paramref name="stores"/>: its word is already the slot.</summary>
    private static Expression? WhenDebugHookAttached(Context ctx, IReadOnlyList<Expression> stores) {
        if (stores.Count == 0 || ctx.DebugHookProp is null)
            return null;
        // IfThen demands a void body. An assignment's type is the stored word, so the
        // copies cannot be the block's tail.
        var body = new List<Expression>(stores.Count + 1);
        body.AddRange(stores);
        body.Add(Empty());
        return IfThen(
            NotEqual(ctx.DebugHookProp, Constant(null, ctx.DebugHookProp.Type)),
            Block(body));
    }

    private static Expression EmitSuspendNode(SuspendNode sn, Context ctx) {
        if (!ctx.IsRootBody)
            throw new UnresumableBodyRejection(
                $"VM compile rejected: SuspendNode '{sn}' is only executable in the compilation-unit body; a compiled function or lambda body emits no resume dispatch.");
        if (ctx.InUnresumable)
            throw new InvalidOperationException(
                $"VM compile rejected: SuspendNode '{sn}' cannot resume inside try, catch, finally, using, or foreach.");
        int step = ctx.StepCounter++;
        ctx.RecordStepNode(step, sn);
        var resumeLabel = ctx.RegisterOrGetResumeLabel(step);
        var innerExpr = CompileNode(sn.Inner, ctx);
        var spill = new List<Expression>();
        var reload = new List<Expression>();
        ctx.SpillBarrier(ctx.RingDepth, spill, reload);
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
        // The reload is the first expression after the label: resume re-enters the
        // method and CLR locals start at zero. It must not follow EmitPcDispatch.
        var resumed = new List<Expression> {
            innerExpr, setCurrentNode, setStatus, saveResumeId, saveFramePos
        };
        resumed.AddRange(spill);
        resumed.Add(Goto(ctx.ExitLabel));
        resumed.Add(Label(resumeLabel));
        resumed.AddRange(reload);
        resumed.Add(clearSuspended);
        return Block(resumed);
    }

    private static Expression EmitParameter(Parameter p, Context ctx) {
        int slot = ctx.AllocSlot();
        if (ctx.TryGetCapture(p, out int capIndex)) {
            return Assign(ctx.RingVar(slot), EmitCaptureCellValue(ctx, capIndex));
        }
        var nullable = ctx.TryClrType(p) ?? (p.TypeReference is null ? null : ctx.TryClrType(p.TypeReference));
        var storage = ctx.Resolve(p);
        if (nullable is not null && Nullable.GetUnderlyingType(nullable) is not null
            && storage.Representation == typeof(long))
            return Assign(
                ctx.RingVar(slot, nullable),
                storage.ReadAs(ctx, nullable));
        return Assign(ctx.RingVar(slot), ParameterValue(p, ctx));
    }

    private static Expression ParameterValue(Parameter p, Context ctx) =>
        RingCodec.ToWord(ctx.Resolve(p).Read(ctx));

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

        stmts.Add(EmitLoopScaffold(
            new LoopPlan(
                "for",
                fl.Label,
                LoopTestPosition.BeforeBody,
                Test: c => CompileConditionAsBool(fl.Condition ?? new Constant(1L), c),
                PerIteration: null,
                Body: c => CompileNode(fl.Body, c),
                AfterContinue: fl.Increment is null ? null : c => CompileNode(fl.Increment, c)),
            ctx));

        if (scoped)
            ctx.PopScope();
        return Block(stmts);
    }

    /// <summary>
    /// ForEachLoop: walk a heap <see cref="System.Collections.IEnumerable"/>,
    /// bind each Current onto the loop variable via <c>BoxToAbi</c>, run the body.
    /// Null / non-enumerable fails loud. Enumerator is disposed when IDisposable.
    /// </summary>
    private static Expression EmitForEachLoop(ForEachLoop fel, Context ctx) {
        ctx.EnterUnresumable();
        try {
            // A foreach yields no ring value: the collection slot and the body's slots are
            // dead when it completes, so the whole emission closes one ring scope.
            using var ringScope = ctx.OpenRingScope();
            ctx.PushScope();
            ctx.DeclareVariable(fel.LoopVariable);
            Expression loopVarCellInit = ctx.NeedsCell(fel.LoopVariable)
                ? EmitUpvalueCellInit(fel.LoopVariable, ctx)
                : Empty();

            var ring = ctx.OpenRing();
            var col = ring.AddValue(CompileNode(fel.Collection, ctx));

            var enumerator = Variable(typeof(System.Collections.IEnumerator), "_en");
            var colObj = Variable(typeof(object), "_col");

            var loop = EmitLoopScaffold(
                new LoopPlan(
                    "foreach",
                    fel.Label,
                    LoopTestPosition.BeforeBody,
                    Test: _ => Call(enumerator, EnumeratorMoveNext),
                    PerIteration: c => c.VariableWrite(fel.LoopVariable, ForEachElementValue(fel, enumerator, c)),
                    Body: c => CompileNode(fel.Body, c),
                    AfterContinue: null),
                ctx);

            ctx.PopScope();

            return ring.Then(Block(
                [enumerator, colObj],
                loopVarCellInit,
                Assign(colObj, Call(ctx.HeapLocal, HeapUnsafeGet,
                    Convert(col.Read, typeof(int)))),
                Assign(enumerator, Call(null, GetEnumeratorOrThrowInfo, colObj)),
                TryFinally(
                    loop,
                    IfThen(
                        TypeIs(enumerator, typeof(IDisposable)),
                        Call(Convert(enumerator, typeof(IDisposable)), IDisposableDispose)))));
        }
        finally {
            ctx.LeaveUnresumable();
        }
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
        ctx.EnterUnresumable();
        try {
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
        finally {
            ctx.LeaveUnresumable();
        }
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
        var result = ring.AddValue(CompileNode(ret.Value, ctx));
        Expression value = result.Read;
        if (ctx.AsyncReturn is not null)
            value = WrapAsyncResult(value, ctx.AsyncReturn, ctx, ret.Value);

        return ring.Then(Block(
            Assign(ArrayAccess(ctx.SlotsLocal, ctx.FramePosLocal), value),
            Assign(ctx.SlotsStackPointer,
                Add(ctx.FramePosLocal, Constant(1))),
            Goto(ctx.ExitLabel)));
    }
}