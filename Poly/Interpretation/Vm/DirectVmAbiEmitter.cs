using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

/// <summary>
/// Direct AST-to-VM-ABI emitter — the primary compilation path.
///
/// Walks the AST after analysis and emits <see cref="Expression"/> trees
/// targeting the bespoke VM ABI (<see cref="VmState"/>, ring registers,
/// <see cref="FrameBase"/>, heap, etc.) — the sole lowering path.
/// </summary>
public static class DirectVmAbiEmitter {
    /// <summary>
    /// Describes a captured variable: the <see cref="Variable"/> node and its
    /// slot index in the <em>outer</em> (capturing) scope's value stack.
    /// </summary>
    private sealed record Capture(Variable Variable, int OuterSlotIndex);

    /// <summary>
    /// Emit a compiled <see cref="VmProgram"/> from an analyzed AST root node,
    /// targeting the bespoke VM ABI directly (no primitives).
    /// </summary>
    /// <param name="root">The AST root node to compile.</param>
    /// <param name="analysis">Analysis result from the standard pipeline (without PrimitiveExpansion).</param>
    /// <param name="mode">Compilation mode for debug/tracing support.</param>
    /// <param name="traceExpressions">Optional writer for expression tree diagnostics.</param>
    /// <returns>A <see cref="VmProgram"/> runnable by <see cref="Interpreter.Execute(VmProgram, Action{VmState})"/>.</returns>
    public static VmProgram Emit(
        Node root,
        AnalysisResult analysis,
        CompilationMode mode = CompilationMode.Normal,
        TextWriter? traceExpressions = null) {

        var ctx = new AbiCtx();
        ctx.Mode = mode;
        ctx.Analysis = analysis;
        var body = new List<Expression>();

        // ── Pre-collect lambdas and pre-compile function bodies ───
        // Collect lambdas to assign sequential LambdaIndex values.
        var lambdas = new List<Lambda>();
        CollectLambdas(root, lambdas);

        // ── Preamble ────────────────────────────────────────────────
        body.Add(Label(ctx.EntryLabel));
        body.Add(Assign(ctx.SlotsLocal, ctx.SlotsInitExpression));
        body.Add(Assign(ctx.HeapLocal, ctx.HeapInitExpression));
        body.Add(Assign(ctx.Registers,
            Coalesce(ctx.Registers, NewArrayBounds(typeof(long), Constant(256)))));
        body.Add(IfThen(
            Equal(Property(ctx.State, "FrameBase"), Constant(-1)),
            Assign(Property(ctx.State, "FrameBase"), Constant(0))));
        body.Add(Assign(ctx.FrameBaseLocal, ctx.FrameBaseInitExpression));

        // Track a small step for legacy/compatibility if needed, but the primary
        // "current position" for the direct path is now the AST node itself
        // (set inside CompileNode / at suspend points).
        if (mode != CompilationMode.NoDebug) {
            body.Add(Assign(ctx.ProgramCounter, Constant(0)));
            ctx.DebugHookProp = Property(ctx.State, nameof(VmState.DebugHook));
        }

        // ── Compile root node ────────────────────────────────────────
        // Enter the top-level activation in the compile-time simulator.
        // The root frame has 0 arguments and 0 known locals at this point;
        // DeclareVariable inside blocks will still assign scope-relative slots.
        ctx.EnterActivation(0, 0);

        var rootExpr = CompileNode(root, ctx);

        // Flush the root result from the ring to the value stack,
        // matching the ABI convention: result at _slots[_fb], SP = _fb + 1.
        // The top ring slot holds the expression result.
        body.Add(rootExpr);
        if (ctx.RingDepth > 0) {
            body.Add(Assign(ArrayAccess(ctx.SlotsLocal, ctx.FrameBaseLocal),
                ctx.RingVar(ctx.RingDepth - 1)));
            body.Add(Assign(ctx.SlotsStackPointer,
                Add(ctx.FrameBaseLocal, Constant(1))));
        }

        // Leave the top-level activation (compile-time simulator bookkeeping).
        ctx.LeaveActivation();

        // ── Exit ─────────────────────────────────────────────────────
        body.Add(Label(ctx.ExitLabel));

        // Determine root value kind for result extraction
        var rootKind = analysis.GetMetadata<ValueRepresentationMetadata>(root)?.Kind;

        // Build and compile the delegate
        var delegateExpr = Lambda<Action<VmState>>(Block(ctx.Locals, body), ctx.State);
        if (traceExpressions != null) {
            traceExpressions.WriteLine("=== Direct AST Emitter Expression Tree ===");
            traceExpressions.WriteLine(DumpTree(delegateExpr));
        }
        var del = delegateExpr.Compile();

        int registerScratchSize = ctx.MaxRingDepth;
        var debugInfo = new VmDebugInfo(ctx.VariableLayouts);
        return new VmProgram(del, registerScratchSize, RootValueKind: rootKind,
            StepNodes: ctx.StepNodes, DebugInfo: debugInfo);
    }

    // ── Compile dispatch ───────────────────────────────────────────

    /// <summary>
    /// Compile a single AST node to an expression that executes it and
    /// leaves its result (if any) on the ring.
    /// In Debug/Normal mode, wraps the node with a <see cref="VmState.DebugInterrupt"/>
    /// check so external code can step through each AST boundary.
    /// </summary>
    private static Expression CompileNode(Node node, AbiCtx ctx) {
        // Assign a step/PC for this node. This serves as the "address" that
        // can be mapped back to the symbolic Node for debuggers, stack traces,
        // variable name resolution, etc.
        int step = ctx.StepCounter++;
        ctx.RecordStepNode(step, node);

        // For the direct path, surface the actual AST node so that VmState can
        // expose the current symbolic position (for debuggers, tracing, and
        // suspend/resume) instead of only a synthetic step/PC.
        var setCurrentNode = Block(
            Assign(Property(ctx.State, nameof(VmState.CurrentAstNode)), Constant(node)),
            Assign(Property(ctx.State, nameof(VmState.CurrentNodeId)), Constant(node.Id, typeof(NodeId?)))
        );

        var body = CompileNodeInner(node, ctx);
        var guarded = WithInterrupt(body, ctx, step);
        return Block(setCurrentNode, guarded);
    }

    /// <summary>
    /// Inner dispatch — no interrupt wrapping. Called by <see cref="CompileNode"/>.
    /// </summary>
    private static Expression CompileNodeInner(Node node, AbiCtx ctx) {
        return node switch {
            // Leaf expressions
            Constant c => EmitConstant(c, ctx),

            // Binary arithmetic
            Add n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, Add, ctx),
            Subtract n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, Subtract, ctx),
            Multiply n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, Multiply, ctx),
            Divide n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, Divide, ctx),
            Modulo n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, Modulo, ctx),

            // Bitwise
            BitwiseAnd n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, And, ctx),
            BitwiseOr n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, Or, ctx),
            BitwiseXor n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, ExclusiveOr, ctx),
            ShiftLeft n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, LeftShift, ctx),
            ShiftRight n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, RightShift, ctx),

            // Comparisons (produce 0/1 long)
            Equal n => EmitComparison(n.LeftHandValue, n.RightHandValue, Equal, ctx, n),
            NotEqual n => EmitComparison(n.LeftHandValue, n.RightHandValue, NotEqual, ctx, n),
            LessThan n => EmitComparison(n.LeftHandValue, n.RightHandValue, LessThan, ctx),
            LessThanOrEqual n => EmitComparison(n.LeftHandValue, n.RightHandValue, LessThanOrEqual, ctx),
            GreaterThan n => EmitComparison(n.LeftHandValue, n.RightHandValue, GreaterThan, ctx),
            GreaterThanOrEqual n => EmitComparison(n.LeftHandValue, n.RightHandValue, GreaterThanOrEqual, ctx),

            // Boolean logical (short-circuit)
            And n => EmitLogicalAnd(n, ctx),
            Or n => EmitLogicalOr(n, ctx),

            // Null coalescing
            Coalesce n => EmitCoalesce(n, ctx),

            // Unary
            Not n => EmitNot(n, ctx),
            UnaryMinus n => EmitUnaryMinus(n, ctx),
            BitwiseNot n => EmitBitwiseNot(n, ctx),

            // Statements / void
            Return r => EmitReturn(r, ctx),
            IfStatement ifStmt => EmitIfStatement(ifStmt, ctx),
            WhileLoop wl => EmitWhileLoop(wl, ctx),
            DoWhileLoop dwl => EmitDoWhileLoop(dwl, ctx),
            ForLoop fl => EmitForLoop(fl, ctx),
            ForEachLoop fel => EmitForEachLoop(fel, ctx),
            BreakStatement bs => EmitBreakStatement(bs, ctx),
            ContinueStatement cs => EmitContinueStatement(cs, ctx),
            GotoStatement gs => EmitGotoStatement(gs, ctx),
            LabelDeclaration ld => EmitLabelDeclaration(ld, ctx),
            ThrowStatement ts => EmitThrow(ts, ctx),
            TryCatchFinally tcf => EmitTryCatchFinally(tcf, ctx),
            UsingStatement us => EmitUsingStatement(us, ctx),
            SuspendNode sn => EmitSuspendNode(sn, ctx),

            // Conditional / ternary
            Conditional c => EmitConditional(c, ctx),

            // Variables and blocks
            Variable v => EmitVariable(v, ctx),
            Assignment a => EmitAssignment(a, ctx),
            Block b => EmitBlock(b, ctx),

            // Closures and lambdas
            Parameter p => EmitParameter(p, ctx),
            Lambda l => EmitLambda(l, ctx),
            Invoke inv => EmitInvoke(inv, ctx),

            // Member access via CLR reflection
            Member m => EmitMember(m, ctx),
            PopCount pc => EmitPopCount(pc, ctx),

            // Type operations
            TypeIs t => EmitTypeIs(t, ctx),
            TypeAs t => EmitTypeAs(t, ctx),
            TypeCast t => EmitTypeCast(t, ctx),
            Await a => EmitAwait(a, ctx),
            Default d => EmitDefault(d, ctx),

            // Allocations and indexing
            New n => EmitNew(n, ctx),
            NewArray n => EmitNewArray(n, ctx),
            IndexAccess n => EmitIndexAccess(n, ctx),

            // Other common
            ThisReference _ => EmitConstant(new Constant(0L), ctx),
            ParameterReference pr => EmitParameterReference(pr, ctx),
            NullForgiving n => CompileNode(n.Operand, ctx),
            StridedSetBits ssb => EmitStridedSetBits(ssb, ctx),

            // Switch as conditional chain
            SwitchStatement sw => EmitSwitch(sw, ctx),

            _ => throw new NotSupportedException(
                $"DirectVmAbiEmitter: unsupported node type {node.GetType().Name}")
        };
    }

    // ── Emit helpers ───────────────────────────────────────────────

    /// <summary>Allocate a new ring slot and emit a constant.
    /// Numeric/primitive values are inlined as long; non-numeric (strings, etc.)
    /// are allocated on the heap at runtime, matching the ABI convention.</summary>
    private static Expression EmitConstant(Constant c, AbiCtx ctx) {
        int slot = ctx.AllocSlot();
        if (TryValueToLong(c.Value, out long val))
            return Assign(ctx.RingVar(slot), Constant(val));
        // float/double: store bit pattern directly in the long ring slot
        if (c.Value is double dbl)
            return Assign(ctx.RingVar(slot), Constant(BitConverter.DoubleToInt64Bits(dbl)));
        if (c.Value is float flt)
            return Assign(ctx.RingVar(slot), Constant(BitConverter.DoubleToInt64Bits(flt)));
        // Non-numeric: allocate on heap
        var allocate = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)),
            Convert(Constant(c.Value), typeof(object)));
        return Assign(ctx.RingVar(slot), Convert(allocate, typeof(long)));
    }

    private static Expression EmitNew(New n, AbiCtx ctx) {
        // Resolve target type
        Type? targetType = n.Type switch {
            ClrTypeReference ctr => ctr.RuntimeType,
            _ => null
        };

        if (targetType is null && ctx.Analysis is not null) {
            var resolvedType = ctx.Analysis.GetResolvedType(n);
            if (resolvedType is ClrTypeDefinition clrDef)
                targetType = clrDef.RuntimeType;
        }

        // Resolve constructor from analysis metadata
        ConstructorInfo? ctor = null;
        if (ctx.Analysis is not null) {
            var resolved = ctx.Analysis.GetResolvedMember(n);
            if (resolved is ClrConstructor clrCtor)
                ctor = clrCtor.ConstructorInfo;
        }

        // If we have a target type but no constructor yet, search for matching ctor
        if (ctor is null && targetType is not null) {
            if (n.Arguments.Length == 0) {
                ctor = targetType.GetConstructor(Type.EmptyTypes);
            }
            else {
                ctor = targetType.GetConstructors()
                    .FirstOrDefault(c => c.GetParameters().Length == n.Arguments.Length);
            }
        }

        if (ctor is not null) {
            // Compile arguments onto the ring — track actual result slots
            int d = ctx.RingDepth;
            var argExprs = new List<Expression>();
            int[] argSlots = new int[n.Arguments.Length];
            for (int i = 0; i < n.Arguments.Length; i++) {
                argExprs.Add(CompileNode(n.Arguments[i], ctx));
                argSlots[i] = ctx.RingDepth - 1;
            }

            var ctorParams = ctor.GetParameters();
            var ctorArgs = new Expression[ctorParams.Length];
            for (int i = 0; i < ctorParams.Length; i++) {
                var ringVal = ctx.RingVar(argSlots[i]);
                var paramType = ctorParams[i].ParameterType;
                if (paramType.IsValueType) {
                    ctorArgs[i] = Convert(ringVal, paramType);
                }
                else if (paramType == typeof(string)) {
                    ctorArgs[i] = Convert(
                        Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                            Convert(ringVal, typeof(int))),
                        paramType);
                }
                else {
                    ctorArgs[i] = Convert(
                        Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                            Convert(ringVal, typeof(int))),
                        paramType);
                }
            }

            var newExpr = New(ctor, ctorArgs);
            var boxed = Convert(newExpr, typeof(object));
            int slot = ctx.AllocSlot();
            var handle = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)), boxed);
            ctx.RingDepth = slot + 1;
            return Block(argExprs.Concat([Assign(ctx.RingVar(slot), Convert(handle, typeof(long)))]));
        }

        // Fallback: store the value directly on heap if it's a constant
        if (targetType is not null && n.Arguments.Length == 0) {
            Expression defaultObj = targetType.IsValueType
                ? Convert(Constant(0L), typeof(object))
                : Constant(null, typeof(object));
            int slot2 = ctx.AllocSlot();
            var handle2 = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)), defaultObj);
            return Assign(ctx.RingVar(slot2), Convert(handle2, typeof(long)));
        }

        // Last resort: empty object[]
        int slot3 = ctx.AllocSlot();
        var placeholder = NewArrayBounds(typeof(object), Constant(0));
        var handle3 = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)),
            Convert(placeholder, typeof(object)));
        return Assign(ctx.RingVar(slot3), Convert(handle3, typeof(long)));
    }

    private static Expression EmitNewArray(NewArray n, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var lenExpr = CompileNode(n.Length, ctx);
        int lenResult = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref lenResult, d, ctx);

        int slot = ctx.AllocSlot();
        // Resolve element type: value types like long[] store unboxed values,
        // reference types and unknown types use object[].
        Type elemType = n.ElementType switch {
            ClrTypeReference ctr when ctr.RuntimeType.IsValueType => ctr.RuntimeType,
            _ => typeof(object)
        };
        var arr = NewArrayBounds(elemType, Convert(ctx.RingVar(lenResult), typeof(int)));
        var handle = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)),
            Convert(arr, typeof(object)));
        return Block(lenExpr, fold, Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));
    }

    private static Expression EmitIndexAccess(IndexAccess n, AbiCtx ctx) {
        int startDepth = ctx.RingDepth;
        var arrExpr = CompileNode(n.Value, ctx);
        int arrSlot = ctx.RingDepth - 1;

        var idxExpr = CompileNode(n.Arguments.Length > 0 ? n.Arguments[0] : new Constant(0), ctx);
        int idxSlot = ctx.RingDepth - 1;

        int outSlot = ctx.AllocSlot();
        // Read raw object from heap, then index based on runtime type.
        // NewArray creates long[] for value types and object[] for ref types,
        // so we handle both.
        var rawObj = Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
            Convert(ctx.RingVar(arrSlot), typeof(int)));
        var idx = Convert(ctx.RingVar(idxSlot), typeof(int));

        var longArr = Variable(typeof(long[]), "_longArr");
        var objArr = Variable(typeof(object[]), "_objArr");
        var valExpr = Condition(
            TypeIs(rawObj, typeof(long[])),
            // long[]: read directly (value is already long)
            Block([longArr],
                Assign(longArr, Convert(rawObj, typeof(long[]))),
                ArrayAccess(longArr, idx)),
            // object[]: read and unbox
            Block([objArr],
                Assign(objArr, Convert(rawObj, typeof(object[]))),
                Convert(ArrayAccess(objArr, idx), typeof(long))));

        return Block(arrExpr, idxExpr,
            Assign(ctx.RingVar(outSlot), valExpr));
    }

    /// <summary>Try to convert a CLR value to the long-based ABI representation.
    /// Returns true for numeric/primitive types that inline directly.</summary>
    private static bool TryValueToLong(object? value, out long result) {
        switch (value) {
            case null: result = 0; return true;
            case long l: result = l; return true;
            case int i: result = i; return true;
            case bool b: result = b ? 1 : 0; return true;
            case short s: result = s; return true;
            case byte bVal: result = bVal; return true;
            default: result = 0; return false;
        }
    }

    /// <summary>
    /// If the operand used more ring slots than expected, fold its result back to slot <paramref name="d"/>
    /// so the caller can reliably find it at <c>RingVar(d)</c>. Returns an expression that does the copy
    /// (or <see cref="Empty()"/> if no copy is needed), and sets <paramref name="resultSlot"/> to <paramref name="d"/>.
    /// </summary>
    private static Expression FoldResultToSlot(ref int resultSlot, int d, AbiCtx ctx) {
        if (resultSlot <= d) return Empty();
        var copy = Assign(ctx.RingVar(d), ctx.RingVar(resultSlot));
        resultSlot = d;
        ctx.RingDepth = d + 1;
        return copy;
    }

    /// <summary>Find the highest set bit position (0-based) for a positive power of two.</summary>
    private static int BitScanReverse(long v) {
        int pos = 0;
        while (v > 1) { v >>= 1; pos++; }
        return pos;
    }

    /// <summary>Binary arithmetic (add, sub, mul, div, mod).</summary>
    private static Expression EmitBinaryArithmetic(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> factory,
        AbiCtx ctx) {
        int d = ctx.RingDepth;
        var leftExpr = CompileNode(left, ctx);
        int leftResult = ctx.RingDepth - 1;

        var rightExpr = CompileNode(right, ctx);
        int rightResult = ctx.RingDepth - 1;

        // Double/float path: reinterpret bits, do IEEE 754 arithmetic, store bits back
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right)) {
            var leftDbl = Call(BitConverterInt64BitsToDouble, ctx.RingVar(leftResult));
            var rightDbl = Call(BitConverterInt64BitsToDouble, ctx.RingVar(rightResult));
            var resultDbl = factory(leftDbl, rightDbl);
            var resultBits = Call(BitConverterDoubleToInt64Bits, resultDbl);
            var result = Assign(ctx.RingVar(d), resultBits);
            ctx.RingDepth = d + 1;
            return Block(leftExpr, rightExpr, result);
        }

        // Integer path — check for power-of-two constant optimizations
        if (right is Constant rc && TryValueToLong(rc.Value, out long rVal) && rVal > 0 && (rVal & (rVal - 1)) == 0) {
            string op = factory.Method.Name;
            // Modulo(x, 2^k) → And(x, k-1)     (avoids expensive rem library call)
            if (op == nameof(Expression.Modulo)) {
                var result = Assign(ctx.RingVar(d),
                    Expression.And(ctx.RingVar(leftResult), Constant(rVal - 1)));
                ctx.RingDepth = d + 1;
                return Block(leftExpr, rightExpr, result);
            }
            // Divide(x, 2^k) → ShiftRight(x, k)  (signed shift)
            if (op == nameof(Expression.Divide)) {
                int k = BitScanReverse(rVal);
                var result = Assign(ctx.RingVar(d),
                    Expression.RightShift(ctx.RingVar(leftResult), Constant(k)));
                ctx.RingDepth = d + 1;
                return Block(leftExpr, rightExpr, result);
            }
            // Multiply(x, 2^k) → ShiftLeft(x, k)
            if (op == nameof(Expression.Multiply)) {
                int k = BitScanReverse(rVal);
                var result = Assign(ctx.RingVar(d),
                    Expression.LeftShift(ctx.RingVar(leftResult), Constant(k)));
                ctx.RingDepth = d + 1;
                return Block(leftExpr, rightExpr, result);
            }
        }

        // Integer path (default behavior)
        Expression rhs = ctx.RingVar(rightResult);
        if (factory == LeftShift || factory == RightShift)
            rhs = Convert(rhs, typeof(int));
        var result2 = Assign(ctx.RingVar(d), factory(ctx.RingVar(leftResult), rhs));
        ctx.RingDepth = d + 1;
        return Block(leftExpr, rightExpr, result2);
    }

    /// <summary>Comparison (eq, neq, lt, gt, etc.) → 0/1 long.
    /// For Equal/NotEqual, heap reference values (strings, objects) are compared
    /// using object.Equals at runtime rather than handle equality.</summary>
    private static Expression EmitComparison(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> comparisonFactory,
        AbiCtx ctx,
        Node? comparisonNode = null) {
        int d = ctx.RingDepth;
        var leftExpr = CompileNode(left, ctx);
        int leftResult = ctx.RingDepth - 1;

        var rightExpr = CompileNode(right, ctx);
        int rightResult = ctx.RingDepth - 1;

        // Detect string comparison: when Equal/NotEqual and both operands are
        // heap references (strings), compare via object.Equals at runtime.
        bool isEquality = comparisonFactory == Equal
                       || comparisonFactory == NotEqual;
        if (isEquality && AreHeapValues(ctx, left, right)) {
            // Read both objects from heap and compare
            var leftObj = Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                Convert(ctx.RingVar(leftResult), typeof(int)));
            var rightObj = Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                Convert(ctx.RingVar(rightResult), typeof(int)));
            var equalCheck = Call(typeof(object).GetMethod("Equals", [typeof(object), typeof(object)])!,
                leftObj, rightObj);
            var result = Assign(ctx.RingVar(d),
                comparisonFactory == Equal
                    ? Condition(equalCheck, Constant(1L), Constant(0L))
                    : Condition(equalCheck, Constant(0L), Constant(1L)));
            ctx.RingDepth = d + 1;
            return Block(leftExpr, rightExpr, result);
        }

        // Double/float comparison: reinterpret bits before comparing
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right)) {
            var leftDbl = Call(BitConverterInt64BitsToDouble, ctx.RingVar(leftResult));
            var rightDbl = Call(BitConverterInt64BitsToDouble, ctx.RingVar(rightResult));
            var result = Assign(ctx.RingVar(d),
                Condition(comparisonFactory(leftDbl, rightDbl),
                    Constant(1L), Constant(0L)));
            ctx.RingDepth = d + 1;
            return Block(leftExpr, rightExpr, result);
        }

        var simpleResult = Assign(
            ctx.RingVar(d),
            Condition(comparisonFactory(ctx.RingVar(leftResult), ctx.RingVar(rightResult)),
                Constant(1L), Constant(0L)));
        ctx.RingDepth = d + 1;
        return Block(leftExpr, rightExpr, simpleResult);
    }

    /// <summary>Check if both nodes likely produce heap reference values that

    /// <summary>Check if both nodes likely produce heap reference values that
    /// should be compared by value rather than handle.</summary>
    private static bool AreHeapValues(AbiCtx ctx, Node left, Node right) {
        // Check analysis metadata for value representation
        if (ctx.Analysis is not null) {
            var leftRep = ctx.Analysis.GetValueRepresentation(left);
            var rightRep = ctx.Analysis.GetValueRepresentation(right);
            if (leftRep == ValueRepresentationKind.HeapRef
                || rightRep == ValueRepresentationKind.HeapRef)
                return true;
        }
        // Heuristic: string constants produce heap handles
        if (left is Constant cl && cl.Value is string) return true;
        if (right is Constant cr && cr.Value is string) return true;
        // Member access on CLR objects may produce heap values
        if (left is Member) return true;
        if (right is Member) return true;
        return false;
    }

    /// <summary>Short-circuit AND: if left is false, skip right.</summary>
    private static Expression EmitLogicalAnd(And and, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var leftExpr = CompileNode(and.LeftHandValue, ctx);
        int leftSlot = ctx.RingDepth - 1;
        var foldLeft = FoldResultToSlot(ref leftSlot, d, ctx);

        int rightStart = ctx.RingDepth;
        var rightExpr = CompileNode(and.RightHandValue, ctx);
        int rightSlot = ctx.RingDepth - 1;

        var result = Assign(ctx.RingVar(d),
            Block(
                leftExpr, foldLeft,
                Condition(
                    Equal(ctx.RingVar(leftSlot), Constant(0L)),
                    Constant(0L),
                    Block(
                        rightExpr,
                        ctx.RingVar(rightSlot)
                    )
                )
            ));
        ctx.RingDepth = d + 1;
        return result;
    }

    /// <summary>Short-circuit OR: if left is true, skip right.</summary>
    private static Expression EmitLogicalOr(Or or, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var leftExpr = CompileNode(or.LeftHandValue, ctx);
        int leftSlot = ctx.RingDepth - 1;
        var foldLeft = FoldResultToSlot(ref leftSlot, d, ctx);

        int rightStart = ctx.RingDepth;
        var rightExpr = CompileNode(or.RightHandValue, ctx);
        int rightSlot = ctx.RingDepth - 1;

        var result = Assign(ctx.RingVar(d),
            Block(
                leftExpr, foldLeft,
                Condition(
                    NotEqual(ctx.RingVar(leftSlot), Constant(0L)),
                    Constant(1L),
                    Block(
                        rightExpr,
                        ctx.RingVar(rightSlot)
                    )
                )
            ));
        ctx.RingDepth = d + 1;
        return result;
    }

    /// <summary>Logical NOT: 0→1, anything else→0.</summary>
    private static Expression EmitNot(Not not, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(not.Value, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);
        var result = Assign(ctx.RingVar(resultSlot),
            Condition(Equal(ctx.RingVar(resultSlot), Constant(0L)), Constant(1L), Constant(0L)));
        ctx.RingDepth = resultSlot + 1;
        return Block(operandExpr, fold, result);
    }

    /// <summary>Unary minus: negate value.</summary>
    private static Expression EmitUnaryMinus(UnaryMinus unaryMinus, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(unaryMinus.Operand, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);
        Expression result;
        if (IsDoubleValue(ctx, unaryMinus.Operand)) {
            var dbl = Call(BitConverterInt64BitsToDouble, ctx.RingVar(resultSlot));
            result = Assign(ctx.RingVar(resultSlot),
                Call(BitConverterDoubleToInt64Bits, Negate(dbl)));
        }
        else {
            result = Assign(ctx.RingVar(resultSlot), Negate(ctx.RingVar(resultSlot)));
        }
        ctx.RingDepth = resultSlot + 1;
        return Block(operandExpr, fold, result);
    }

    private static Expression EmitBitwiseNot(BitwiseNot n, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(n.Operand, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);
        var result = Assign(ctx.RingVar(resultSlot), Not(ctx.RingVar(resultSlot)));
        ctx.RingDepth = resultSlot + 1;
        return Block(operandExpr, fold, result);
    }

    /// <summary>PopCount via System.Numerics.BitOperations.PopCount.</summary>
    private static Expression EmitPopCount(PopCount pc, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(pc.Operand, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);
        var call = Call(null,
            typeof(System.Numerics.BitOperations).GetMethod(nameof(System.Numerics.BitOperations.PopCount), [typeof(ulong)])!,
            Convert(ctx.RingVar(resultSlot), typeof(ulong)));
        var result = Assign(ctx.RingVar(resultSlot), Convert(call, typeof(long)));
        ctx.RingDepth = resultSlot + 1;
        return Block(operandExpr, fold, result);
    }

    /// <summary>Member access via CLR reflection: resolve from analysis metadata
    /// and emit a property getter, field read, or method call.</summary>
    private static Expression EmitMember(Member m, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var instanceExpr = CompileNode(m.Value, ctx);
        int instanceSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref instanceSlot, d, ctx);

        var resolved = ctx.Analysis?.GetResolvedMember(m);

        // Static member — no instance needed
        if (resolved?.LifetimeModifier == LifetimeModifier.Static) {
            return EmitResolvedMember(resolved, null, d, ctx, Block(instanceExpr, fold));
        }

        if (resolved is not null) {
            var declaringTypeDef = resolved.DeclaringTypeDefinition;
            bool isValueType = declaringTypeDef is ClrTypeDefinition clrDef
                && clrDef.RuntimeType.IsValueType;

            Expression instanceObj;
            if (isValueType) {
                instanceObj = Convert(ctx.RingVar(instanceSlot), typeof(object));
            }
            else {
                instanceObj = Call(ctx.HeapLocal,
                    typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                    Convert(ctx.RingVar(instanceSlot), typeof(int)));
            }
            return EmitResolvedMember(resolved, instanceObj, d, ctx, Block(instanceExpr, fold));
        }

        // No metadata — fallback passthrough
        return instanceExpr;
    }

    /// <summary>Emit the resolved member access expression and store the result
    /// on the ring.  Handles property getters, field reads, methods (e.g. ToString), and constructors.</summary>
    private static Expression EmitResolvedMember(
        ITypeMember resolved,
        Expression? instanceObj,
        int resultSlot,
        AbiCtx ctx,
        Expression instanceExpr) {

        // Property access via Read delegate
        if (resolved is ITypeProperty prop && prop.Read is not null) {
            var emptyArgs = NewArrayBounds(typeof(object), Constant(0));
            Expression readCall = instanceObj is not null
                ? Invoke(Constant(prop.Read), instanceObj, emptyArgs)
                : Invoke(Constant(prop.Read), Constant(null, typeof(object)), emptyArgs);
            return Block(instanceExpr, Assign(ctx.RingVar(resultSlot),
                ConvertMemberResult(readCall, resolved, ctx)));
        }

        // Field access via Read delegate
        if (resolved is ITypeField field && field.Read is not null) {
            var emptyArgs = NewArrayBounds(typeof(object), Constant(0));
            Expression readCall = instanceObj is not null
                ? Invoke(Constant(field.Read), instanceObj, emptyArgs)
                : Invoke(Constant(field.Read), Constant(null, typeof(object)), emptyArgs);
            return Block(instanceExpr, Assign(ctx.RingVar(resultSlot),
                ConvertMemberResult(readCall, resolved, ctx)));
        }

        // Method access (e.g. ToString, GetHashCode) — invoke via MethodInfo
        if (resolved is ITypeMethod method) {
            // Get the MethodInfo from CLR metadata
            var clrMethod = resolved as ClrMethod;
            var methodInfo = clrMethod?.MethodInfo;
            if (methodInfo is not null && methodInfo.GetParameters().Length == 0) {
                // Parameterless method call: instance.Method()
                // For value types, the instance may be boxed (object) — unbox first
                Expression? instanceForCall = instanceObj;
                if (instanceObj is not null && methodInfo.DeclaringType?.IsValueType == true) {
                    // Unbox: Convert(instanceObj, declaringType) casts object→Int64 etc.
                    instanceForCall = Convert(instanceObj, methodInfo.DeclaringType);
                }
                Expression resultExpr = instanceForCall is not null
                    ? Call(instanceForCall, methodInfo)
                    : Call(null, methodInfo);
                return Block(instanceExpr, Assign(ctx.RingVar(resultSlot),
                    ConvertMemberResult(resultExpr, resolved, ctx)));
            }
        }

        // Fallback: return instance
        return instanceExpr;
    }

    /// <summary>Convert a member access result (object?) to the ring ABI (long).
    /// Value types are unboxed to long; reference types are heap-allocated.</summary>
    private static Expression ConvertMemberResult(Expression readCall, ITypeMember resolved, AbiCtx ctx) {
        // Try to determine if the member type is a value type via CLR metadata.
        var memberTypeDef = resolved.MemberTypeDefinition;
        if (memberTypeDef is ClrTypeDefinition clrDef) {
            var clrType = clrDef.RuntimeType;
            if (clrType.IsValueType) {
                // Unbox: (long)(T)(object?)readCall
                return Convert(Convert(readCall, clrType), typeof(long));
            }
        }
        // Reference type: allocate on heap and return handle
        var handle = Call(ctx.HeapLocal,
            typeof(Heap).GetMethod(nameof(Heap.Allocate))!,
            readCall);
        return Convert(handle, typeof(long));
    }

    /// <summary>TypeIs: check if the operand's heap object is assignable to the target type.</summary>
    private static Expression EmitTypeIs(TypeIs t, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(t.Operand, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);

        // Resolve target type from TypeReference via analysis or CLR type lookup
        Type? targetType = null;
        if (t.TargetTypeReference is ClrTypeReference clrRef) {
            targetType = clrRef.RuntimeType;
        }
        // Try analysis metadata fallback
        if (targetType is null && ctx.Analysis is not null) {
            var resolvedType = ctx.Analysis.GetResolvedType(t);
            if (resolvedType is ClrTypeDefinition clrDef) {
                targetType = clrDef.RuntimeType;
            }
        }

        if (targetType is null) {
            // Cannot resolve — return 0 (false)
            return Block(operandExpr, fold, Assign(ctx.RingVar(resultSlot), Constant(0L)));
        }

        // Read heap object and check type: _heap.UnsafeGet((int)handle) is TargetType
        var heapObj = Call(ctx.HeapLocal,
            typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
            Convert(ctx.RingVar(resultSlot), typeof(int)));
        var typeCheck = TypeIs(heapObj, targetType);
        var result = Condition(typeCheck, Constant(1L), Constant(0L));
        return Block(operandExpr, fold, Assign(ctx.RingVar(resultSlot), result));
    }

    /// <summary>TypeAs: Expression.TypeAs(operand, targetType).</summary>
    private static Expression EmitTypeAs(TypeAs t, AbiCtx ctx) {
        // Passthrough for POC — same as EmitMember passthrough
        return CompileNode(t.Operand, ctx);
    }

    /// <summary>TypeCast: Expression.Convert(operand, targetType).</summary>
    private static Expression EmitTypeCast(TypeCast t, AbiCtx ctx) {
        // Passthrough for POC — values are already long in the ABI
        return CompileNode(t.Operand, ctx);
    }

    /// <summary>Await: synchronous extraction via GetAwaiter().GetResult().</summary>
    private static Expression EmitAwait(Await a, AbiCtx ctx) {
        // Passthrough for POC — the operand value is the result
        return CompileNode(a.Operand, ctx);
    }

    /// <summary>Default: produce the default value for a type (0 for long/scalar).</summary>
    private static Expression EmitDefault(Default d, AbiCtx ctx) {
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot), Constant(0L));
    }

    /// <summary>ParameterReference: resolve to the referenced Parameter node and emit it.</summary>
    private static Expression EmitParameterReference(ParameterReference pr, AbiCtx ctx) {
        // Try to resolve the referenced Parameter via analysis metadata.
        // The DomainExpressionLoweringPass may produce Member(ParameterReference, ...)
        // where ParameterReference aliases a concrete Parameter node from the lowering.
        // Fall back to 0L if unresolvable.
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot), Constant(0L));
    }

    /// <summary>StridedSetBits: bit-level strided set (handle, start, step, limit).</summary>
    private static Expression EmitStridedSetBits(StridedSetBits ssb, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var arrExpr = CompileNode(ssb.Array, ctx);
        int arrSlot = ctx.RingDepth - 1;
        var startExpr = CompileNode(ssb.StartValue, ctx);
        int startSlot = ctx.RingDepth - 1;
        var stepExpr = CompileNode(ssb.Step, ctx);
        int stepSlot = ctx.RingDepth - 1;
        var limitExpr = CompileNode(ssb.Limit, ctx);
        int limitSlot = ctx.RingDepth - 1;

        // Fold all four operands to consecutive slots starting at d
        var foldArr = FoldResultToSlot(ref arrSlot, d, ctx);
        var foldStart = FoldResultToSlot(ref startSlot, d + 1, ctx);
        var foldStep = FoldResultToSlot(ref stepSlot, d + 2, ctx);
        var foldLimit = FoldResultToSlot(ref limitSlot, d + 3, ctx);
        ctx.RingDepth = d + 4;

        // ABI-level strided set
        var arrObj = Convert(ArrayAccess(ctx.HeapRawSlots, Convert(ctx.RingVar(arrSlot), typeof(int))), typeof(long[]));
        var j = Variable(typeof(long), "_bits_j");
        var loopStart = Label("_stride_loop");
        var loopEnd = Label("_stride_done");
        var loopBody = Block(
            Assign(ArrayAccess(arrObj, Convert(RightShift(j, Constant(6)), typeof(int))),
                Or(ArrayAccess(arrObj, Convert(RightShift(j, Constant(6)), typeof(int))),
                    LeftShift(Constant(1L), Convert(And(j, Constant(63L)), typeof(int))))),
            Assign(j, Add(j, ctx.RingVar(stepSlot))), // j += step
            IfThen(GreaterThan(j, ctx.RingVar(limitSlot)), Goto(loopEnd)),
            Goto(loopStart)
        );
        var result = Block(
            [j],
            Assign(j, ctx.RingVar(startSlot)), // j = start
            Label(loopStart),
            loopBody,
            Label(loopEnd)
        );
        return Block(arrExpr, startExpr, stepExpr, limitExpr, result);
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

        var loopBody = new List<Expression>();
        var condExpr = CompileNode(condition, ctx);
        int condSlot = ctx.RingDepth - 1;
        var foldCond = FoldResultToSlot(ref condSlot, d, ctx);
        ctx.RingDepth = condSlot + 1;

        loopBody.Add(condExpr);
        loopBody.Add(foldCond);
        loopBody.Add(IfThen(Equal(ctx.RingVar(condSlot), Constant(0L)), Goto(breakLabel)));
        loopBody.Add(bodyExpr);
        if (incrementExpr != null) loopBody.Add(incrementExpr);
        loopBody.Add(Label(continueLabel));

        stmts.Add(Loop(Block(loopBody), breakLabel));
        ctx.PopLoopScope();
        ctx.RingDepth = d + 1;
        return Block(stmts);
    }

    /// <summary>ForEachLoop: compile the body (POC — real enumeration requires CLR interop).</summary>
    private static Expression EmitForEachLoop(ForEachLoop fel, AbiCtx ctx) {
        var breakLabel = Label("foreach_break");
        var continueLabel = Label("foreach_continue");
        ctx.PushLoopScope(breakLabel, continueLabel);

        var collectionExpr = CompileNode(fel.Collection, ctx);
        // collection result is not used (POC — enumeration stub)

        int bodyDepth = ctx.RingDepth;
        var bodyExpr = CompileNode(fel.Body, ctx);
        ctx.RingDepth = bodyDepth;

        ctx.PopLoopScope();
        ctx.RingDepth = 0;
        return Block(collectionExpr, bodyExpr);
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

    /// <summary>UsingStatement: lower to try/finally with Dispose call.</summary>
    private static Expression EmitUsingStatement(UsingStatement us, AbiCtx ctx) {
        // Evaluate resource, wrap body in try/finally with Dispose
        var resourceExpr = CompileNode(us.Resource, ctx);
        var bodyExpr = CompileNode(us.Body, ctx);

        // Dispose call on the resource handle (simplified — real impl casts to IDisposable via heap)
        var resourceSlot = ctx.RingDepth - 1;
        var disposeMethod = typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!;
        // For POC, just wrap body in try/finally (dispose omitted at ABI level)
        return Block(resourceExpr, TryFinally(bodyExpr, Empty()));
    }

    // ── Statements ─────────────────────────────────────────────────

    /// <summary>Return statement: write value to frame slot, set SP, jump to exit.</summary>
    private static Expression EmitReturn(Return ret, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var retVal = ret.Value ?? throw new InvalidOperationException("Return with null value");
        var valueExpr = CompileNode(retVal, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);

        // Write to _slots[_fb], set SP = _fb + 1, goto exit
        return Block(
            valueExpr, fold,
            Assign(ArrayAccess(ctx.SlotsLocal, ctx.FrameBaseLocal), ctx.RingVar(resultSlot)),
            Assign(ctx.SlotsStackPointer,
                Add(ctx.FrameBaseLocal, Constant(1))),
            Goto(ctx.ExitLabel));
    }

    /// <summary>Variable reference: read from value stack or from
    /// closure capture array (if this is a captured upvalue).
    /// Leaving the value on the ring for expression chaining.</summary>
    private static Expression EmitVariable(Variable v, AbiCtx ctx) {
        // Check capture (upvalue) first — used inside lambda bodies
        if (ctx.TryGetCapture(v, out int capIndex)) {
            int slot = ctx.AllocSlot();
            // Read from heap[ state.ClosureHandle ][ capIndex + 1 ]
            // The closure array is object[] stored at the handle in heap raw slots.
            var closureHandle = ctx.ClosureHandle;
            var closureArr = Convert(
                ArrayAccess(ctx.HeapRawSlots, Convert(closureHandle, typeof(int))),
                typeof(object[]));
            var captured = Convert(
                ArrayAccess(closureArr, Constant(capIndex + 1)),
                typeof(long));
            return Assign(ctx.RingVar(slot), captured);
        }
        // Local variable on value stack — read via compile-time frame offset
        int slot2 = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot2), ctx.VariableRead(v));
    }

    /// <summary>Assignment: evaluate RHS, store to variable (local or capture),
    /// array index, or other writable target, push value on ring.</summary>
    private static Expression EmitAssignment(Assignment a, AbiCtx ctx) {
        // Array element assignment: arr[index] = value
        if (a.Destination is IndexAccess indexAccess) {
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

            // Runtime type check: long[] or object[]
            var longArr = Variable(typeof(long[]), "_assignLongArr");
            var objArr = Variable(typeof(object[]), "_assignObjArr");
            var store = IfThenElse(
                TypeIs(rawObj, typeof(long[])),
                // long[]: write directly
                Block([longArr],
                    Assign(longArr, Convert(rawObj, typeof(long[]))),
                    Assign(ArrayAccess(longArr, idx), val)),
                // object[]: box and write
                Block([objArr],
                    Assign(objArr, Convert(rawObj, typeof(object[]))),
                    Assign(ArrayAccess(objArr, idx), Convert(val, typeof(object)))));

            // Leave the assigned value on the ring
            ctx.RingDepth = arrSlot + 1;
            return Block(arrExpr, idxExpr, valExpr, store,
                Assign(ctx.RingVar(arrSlot), val));
        }

        if (a.Destination is not Variable destVar) {
            throw new NotSupportedException(
                $"Assignment destination must be a Variable or IndexAccess, got {a.Destination.GetType().Name}");
        }

        // Check capture (upvalue) first
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

        // Local variable — write via compile-time frame offset
        int d2 = ctx.RingDepth;
        var valueExpr2 = CompileNode(a.Value, ctx);
        // Result is at ctx.RingDepth - 1 — use that slot (not d2), because
        // complex value expressions (NewArray, etc.) may allocate multiple slots.
        int resultSlot = ctx.RingDepth - 1;

        var result = ctx.VariableWrite(destVar, ctx.RingVar(resultSlot));
        ctx.RingDepth = resultSlot + 1;
        return Block(valueExpr2, result);
    }

    /// <summary>Block: compile statements sequentially in a child scope.</summary>
    private static Expression EmitBlock(Block block, AbiCtx ctx) {
        ctx.PushScope();
        var varInitExprs = new List<Expression>();

        foreach (var v in block.Variables) {
            if (v is Variable variable) {
                // Declare variable: allocates a register file slot
                ctx.DeclareVariable(variable);
                // Initialize to 0 (ABI convention for long slots).
                // This writes to the register file, not _slots.
                varInitExprs.Add(Assign(ctx.VariableRead(variable), Constant(0L)));
            }
        }

        var stmtExprs = new List<Expression>();
        for (int i = 0; i < block.Nodes.Count; i++) {
            stmtExprs.Add(CompileNode(block.Nodes[i], ctx));
        }

        // Flush register file back to _slots before scope exit.
        // This gives the JIT a clear load-compute-store pattern.
        var stores = ctx.EmitScopeStores();
        ctx.PopScope();
        return Block(Block(varInitExprs), Block(stmtExprs), Block(stores));
    }

    /// <summary>If statement: conditionally execute branches.
    /// For this spike, handles only void branches (no value produced on ring).
    /// Ring depth converges to the pre-condition depth.</summary>
    private static Expression EmitIfStatement(IfStatement ifStmt, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var condExpr = CompileNode(ifStmt.Condition, ctx);
        int condSlot = ctx.RingDepth - 1;
        var foldCond = FoldResultToSlot(ref condSlot, d, ctx);

        ctx.RingDepth = condSlot + 1;
        var thenBody = CompileNode(ifStmt.ThenBranch, ctx);

        Expression? elseBody = null;
        var elseNode = ifStmt.ElseBranch;
        if (elseNode is not null) {
            ctx.RingDepth = condSlot + 1;
            elseBody = CompileNode(elseNode, ctx);
        }

        // IfStatement is a statement (void) — no result left on ring.
        ctx.RingDepth = d;

        return Block(
            condExpr, foldCond,
            IfThenElse(
                NotEqual(ctx.RingVar(condSlot), Constant(0L)),
                thenBody,
                elseBody ?? Empty()));
    }

    /// <summary>Conditional (ternary): condition ? true : false. Leaves result on ring.</summary>
    private static Expression EmitConditional(Conditional c, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var condExpr = CompileNode(c.Condition, ctx);
        int condSlot = ctx.RingDepth - 1;
        var foldCond = FoldResultToSlot(ref condSlot, d, ctx);
        ctx.RingDepth = condSlot + 1;

        var trueExpr = CompileNode(c.IfTrue, ctx);
        int trueResultSlot = ctx.RingDepth - 1;

        var falseExpr = CompileNode(c.IfFalse, ctx);
        int falseResultSlot = ctx.RingDepth - 1;

        // Use the condition value from the folded slot
        var result = Condition(
            NotEqual(ctx.RingVar(condSlot), Constant(0L)),
            ctx.RingVar(trueResultSlot),
            ctx.RingVar(falseResultSlot)
        );
        int slot = ctx.AllocSlot();
        return Block(condExpr, foldCond, trueExpr, falseExpr,
            Assign(ctx.RingVar(slot), result));
    }

    private static Expression EmitCoalesce(Coalesce n, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var leftExpr = CompileNode(n.LeftHandValue, ctx);
        int leftSlot = ctx.RingDepth - 1;
        var foldLeft = FoldResultToSlot(ref leftSlot, d, ctx);
        ctx.RingDepth = leftSlot + 1;

        int rightStart = ctx.RingDepth;
        var rightExpr = CompileNode(n.RightHandValue, ctx);
        int rightSlot = ctx.RingDepth - 1;

        // If left != 0 use left else right (0 represents "null" or falsy in ABI)
        var result = Condition(
            NotEqual(ctx.RingVar(leftSlot), Constant(0L)),
            ctx.RingVar(leftSlot),
            ctx.RingVar(rightSlot)
        );
        int outSlot = ctx.AllocSlot();
        return Block(leftExpr, foldLeft, rightExpr,
            Assign(ctx.RingVar(outSlot), result));
    }

    private static Expression EmitSwitch(SwitchStatement sw, AbiCtx ctx) {
        // Lower to chained conditionals matching the pattern used by EmitConditional:
        // pre-compile all patterns and bodies into ring slots, then build nested
        // LINQ Condition expressions that select the correct body result.
        int d = ctx.RingDepth;
        var valueExpr = CompileNode(sw.Value, ctx);
        int valSlot = ctx.RingDepth - 1;
        var foldVal = FoldResultToSlot(ref valSlot, d, ctx);
        ctx.RingDepth = valSlot + 1;

        // Pre-compile default case (evaluated unconditionally; result selected via Condition).
        Expression defExpr;
        int defSlot;
        if (sw.DefaultCase != null) {
            int defDepth = ctx.RingDepth;
            defExpr = CompileNode(sw.DefaultCase, ctx);
            ctx.RingDepth = defDepth + 1;
            defSlot = defDepth;
        }
        else {
            defSlot = ctx.AllocSlot();
            defExpr = Assign(ctx.RingVar(defSlot), Constant(0L));
        }

        // Pre-compile all cases (patterns and bodies) in order.
        var compiledCases = new (Expression pExpr, int pSlot, Expression bExpr, int bSlot)[sw.Cases.Count];
        for (int i = 0; i < sw.Cases.Count; i++) {
            var c = sw.Cases[i];

            int pDepth = ctx.RingDepth;
            var pExpr = CompileNode(c.Pattern, ctx);
            ctx.RingDepth = pDepth + 1;
            int pSlot = pDepth;

            int bDepth = ctx.RingDepth;
            var bExpr = CompileNode(c.Body, ctx);
            ctx.RingDepth = bDepth + 1;
            int bSlot = bDepth;

            compiledCases[i] = (pExpr, pSlot, bExpr, bSlot);
        }

        // Build nested Condition expressions from last case to first.
        Expression resultExpr = ctx.RingVar(defSlot);
        for (int i = sw.Cases.Count - 1; i >= 0; i--) {
            var (_, pSlot, _, bSlot) = compiledCases[i];
            resultExpr = Condition(
                Expression.Equal(ctx.RingVar(valSlot), ctx.RingVar(pSlot)),
                ctx.RingVar(bSlot),
                resultExpr
            );
        }

        // Assign the final selected value to a ring slot.
        int outSlot = ctx.AllocSlot();
        ctx.RingDepth = outSlot + 1;

        var allExprs = new List<Expression> { valueExpr, defExpr };
        foreach (var (pExpr, _, bExpr, _) in compiledCases) {
            allExprs.Add(pExpr);
            allExprs.Add(bExpr);
        }
        allExprs.Add(foldVal);
        allExprs.Add(Assign(ctx.RingVar(outSlot), resultExpr));

        return Block(allExprs);
    }

    /// <summary>While loop: evaluate condition, execute body, repeat.</summary>
    private static Expression EmitWhileLoop(WhileLoop wl, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var breakLabel = Label("wl_break");
        var continueLabel = Label("wl_continue");
        ctx.PushLoopScope(breakLabel, continueLabel);

        var condExpr = CompileNode(wl.Condition, ctx);
        int condSlot = ctx.RingDepth - 1;
        var foldCond = FoldResultToSlot(ref condSlot, d, ctx);
        ctx.RingDepth = condSlot + 1;

        // Body: must be ring-neutral (no net value left)
        int bodyDepth = ctx.RingDepth;
        var bodyExpr = CompileNode(wl.Body, ctx);
        // Reset ring depth after body
        ctx.RingDepth = bodyDepth;

        var loopBody = Block(
            condExpr, foldCond,
            IfThen(
                Equal(ctx.RingVar(condSlot), Constant(0L)),
                Goto(breakLabel)),
            bodyExpr,
            Label(continueLabel));

        var result = Loop(loopBody, breakLabel);
        ctx.PopLoopScope();
        ctx.RingDepth = d; // loop produces nothing
        return result;
    }

    /// <summary>Do-while: body then condition.</summary>
    private static Expression EmitDoWhileLoop(DoWhileLoop dwl, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var breakLabel = Label("dwl_break");
        var continueLabel = Label("dwl_continue");
        ctx.PushLoopScope(breakLabel, continueLabel);

        int bodyDepth = ctx.RingDepth;
        var bodyExpr = CompileNode(dwl.Body, ctx);
        ctx.RingDepth = bodyDepth;

        var condExpr = CompileNode(dwl.Condition, ctx);
        int condSlot = ctx.RingDepth - 1;
        var foldCond = FoldResultToSlot(ref condSlot, d, ctx);
        ctx.RingDepth = condSlot + 1;

        var loopBody = Block(
            bodyExpr,
            Label(continueLabel),
            condExpr, foldCond,
            IfThen(
                Equal(ctx.RingVar(condSlot), Constant(0L)),
                Goto(breakLabel))
        );

        var result = Loop(loopBody, breakLabel);
        ctx.PopLoopScope();
        ctx.RingDepth = d;
        return result;
    }

    /// <summary>Throw statement: compile the operand for side effects if any,
    /// then throw a CLR Exception.</summary>
    private static Expression EmitThrow(ThrowStatement ts, AbiCtx ctx) {
        // If the exception operand is a New node (creating a real CLR exception),
        // compile it and read the heap result. Otherwise, throw a generic Exception.
        if (ts.Exception is New) {
            int d = ctx.RingDepth;
            var compiled = CompileNode(ts.Exception, ctx); // result at ctx.RingDepth-1
            int resultSlot = ctx.RingDepth - 1;

            var heapObj = Call(ctx.HeapLocal,
                typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                Convert(ctx.RingVar(resultSlot), typeof(int)));
            var exVar = Variable(typeof(Exception), "_thrownEx");
            return Block(
                [exVar],
                compiled,
                Assign(exVar, Convert(heapObj, typeof(Exception))),
                Throw(exVar));
        }

        // For non-New operands (constants, expressions), just throw generic Exception
        var sideEffects = CompileNode(ts.Exception, ctx);
        return Block(sideEffects, Throw(New(typeof(Exception))));
    }

    /// <summary>
    /// TryCatchFinally: use native CLR structured EH instead of flat markers + side table.
    /// This is much simpler than the primitive path's reconstruction.
    /// </summary>
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
            var exType = typeof(Exception);
            var exParam = Parameter(exType, clause.VariableName ?? "ex");

            // For POC binding: allocate the exception on the heap and store handle to a ring slot.
            // The catch body is compiled; if it references the variable by name in tests, we use a synthetic.
            ctx.PushScope();
            Variable? synthetic = null;
            if (!string.IsNullOrEmpty(clause.VariableName)) {
                synthetic = new Variable(clause.VariableName);
                ctx.DeclareVariable(synthetic);
            }

            var bodyExpr = CompileNode(clause.Body, ctx);

            // Allocate handle for the ex so ABI code can see it as a "value".
            var allocate = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)), Convert(exParam, typeof(object)));
            var handle = Convert(allocate, typeof(long));

            Expression catchBodyExpr = bodyExpr;
            if (synthetic != null) {
                // Store the handle so EmitVariable for this synthetic can find it.
                // Use compile-time frame offset for the write.
                catchBodyExpr = Block(
                    ctx.VariableWrite(synthetic, handle),
                    bodyExpr
                );
            }

            // Emit scope stores before popping, then PopScope.
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

    /// <summary>
    /// SuspendNode: evaluate inner, set Suspended status, save step as PC, jump to exit.
    /// Supports non-trivial suspend/resume validation (e.g. inside loops with captures).
    /// </summary>
    private static Expression EmitSuspendNode(SuspendNode sn, AbiCtx ctx) {
        var innerExpr = CompileNode(sn.Inner, ctx);

        // Explicitly set the source AST node for this suspension point.
        // This gives VmState a direct reference to the symbolic position
        // (no need for a reverse PC->node map in the common case for the direct path).
        var setCurrentNode = Block(
            Assign(Property(ctx.State, nameof(VmState.CurrentAstNode)), Constant(sn)),
            Assign(Property(ctx.State, nameof(VmState.CurrentNodeId)), Constant(sn.Id, typeof(NodeId?)))
        );

        var setStatus = Assign(
            Property(ctx.State, nameof(VmState.Status)),
            Constant(InterpreterStatus.Suspended));

        // We can still maintain a small resume id / step for the dispatch logic
        // if using a label-based resume map, but the node itself is now primary
        // for "where are we symbolically".
        var saveResumeId = Assign(ctx.ProgramCounter, Constant(ctx.StepCounter));

        return Block(
            innerExpr,
            setCurrentNode,
            setStatus,
            saveResumeId,
            Goto(ctx.ExitLabel)
        );
    }

    /// <summary>Parameter: read from the value-stack slot set up by the caller
    /// before the function body is invoked. Parameters are at _slots[_fb + paramIndex]
    /// (adjusted by ParamSlotOffset for lambda bodies where parameters live before
    /// local variables). Top-level parameters (passed via <c>SetArgs</c>) are
    /// auto-declared if not already registered in a lambda scope.</summary>
    private static Expression EmitParameter(Parameter p, AbiCtx ctx) {
        if (!ctx.TryGetParameterSlot(p, out int paramIdx)) {
            // Auto-declare as a top-level parameter (e.g. from SetArgs in Policy evaluation).
            paramIdx = ctx.DeclareParameter(p);
        }
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot), ctx.ParameterRead(paramIdx));
    }

    /// <summary>
    /// Lambda: detect captures from the outer scope and allocate a closure
    /// carrying <c>[funcIndex, capture0, capture1, ...]</c>.
    /// For the spike, the function body is compiled inline in the outer
    /// expression (no separate delegate).  This avoids NRE issues with
    /// closure handle access in standalone function bodies.
    /// </summary>
    private static Expression EmitLambda(Lambda lambda, AbiCtx ctx) {
        if (lambda.LambdaIndex < 0)
            throw new InvalidOperationException("Lambda.LambdaIndex not set during lambda collection");

        // 1. Detect captures — variables in the body that belong to outer scopes
        var captures = FindCaptures(lambda.Body, ctx);

        // Register captures on the context so EmitVariable can find them
        for (int i = 0; i < captures.Count; i++)
            ctx.DeclareCapture(captures[i].Variable, i);

        // 2. Allocate closure: object[] { (long)funcIndex, (long)capture0, ... }
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

        var handle = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)),
            Convert(closureArrVar, typeof(object)));
        int slot = ctx.AllocSlot();
        body.Add(Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));
        return Block([closureArrVar], body);
    }

    /// <summary>
    /// Recursively walk a lambda body and collect all <see cref="Variable"/> nodes
    /// that resolve to the outer context's scope (i.e., captures).
    /// </summary>
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
            // Don't recurse into Variable's children (it has a Value expression
            // that isn't part of variable resolution).
            return;
        }
        foreach (var child in node.Children) {
            if (child is not null)
                FindCapturesRecursive(child, outerCtx, result, seen);
        }
    }

    /// <summary>
    /// Invoke: for Lambda targets, compile the body inline (no separate delegate).
    /// </summary>
    private static Expression EmitInvoke(Invoke invoke, AbiCtx ctx) {
        // Handle Invoke(Member(instance, "Method"), args) — resolve the method
        // and call it directly via CLR reflection, bypassing full lambda handling.
        if (invoke.Delegate is Member member) {
            var resolved = ctx.Analysis?.GetResolvedMember(member);
            if (resolved is ITypeMethod method) {
                var clrMethod = resolved as ClrMethod;
                var methodInfo = clrMethod?.MethodInfo;
                if (methodInfo is not null) {
                    int d = ctx.RingDepth;
                    // Compile instance (for instance methods) or null (for static)
                    bool isStatic = resolved.LifetimeModifier == LifetimeModifier.Static;
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
                            methodArgs[i] = Convert(ringVal, paramType);
                        }
                        else if (paramType == typeof(string)) {
                            methodArgs[i] = Convert(
                                Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                                    Convert(ringVal, typeof(int))),
                                paramType);
                        }
                        else {
                            methodArgs[i] = Convert(
                                Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
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
                            typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                            Convert(ctx.RingVar(instanceSlot), typeof(int)));
                        callExpr = Call(instanceObj, methodInfo, methodArgs);
                    }

                    int slot = ctx.AllocSlot();
                    ctx.RingDepth = slot + 1;

                    // Convert result to ABI (value types unboxed, ref types heap-allocated)
                    var resultType = methodInfo.ReturnType;
                    if (resultType == typeof(void)) {
                        return Block(argExprs.Concat([callExpr]));
                    }
                    if (resultType.IsValueType) {
                        return Block(argExprs.Concat([
                            Assign(ctx.RingVar(slot), Convert(callExpr, typeof(long)))]));
                    }
                    // Reference type return: allocate on heap
                    var heapHandle = Call(ctx.HeapLocal,
                        typeof(Heap).GetMethod(nameof(Heap.Allocate))!,
                        Convert(callExpr, typeof(object)));
                    return Block(argExprs.Concat([
                        Assign(ctx.RingVar(slot), Convert(heapHandle, typeof(long)))]));
                }
            }
            // If method resolution fails, throw a clear error
            throw new NotSupportedException(
                $"DirectVmAbiEmitter: Invoke with Member delegate '{member.MemberName}' " +
                $"could not resolve to a method. Ensure TypeAndMemberResolver is in the pipeline.");
        }

        if (invoke.Delegate is Lambda lambda) {

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
            var fbProp = Property(ctx.State, nameof(VmState.FrameBase));
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
            //   [_callSp + 0] = PreviousFP
            //   [_callSp + 1] = SavedSP
            //   [_callSp + 2] = arg0, [_callSp + 3] = arg1, ...
            //   _fb = callSp + 2 + max(args.Length, 1)
            //
            // ParameterRead uses _fb - ParamSlotOffset + paramIdx which resolves to
            // _slots[callSp + 2 + paramIdx] — correct with the 2-word header.
            var callSp = Variable(typeof(int), "_callSp");
            preBody.Add(Assign(callSp, spProp));

            int headerSize = invoke.Arguments.Length > 0 ? 2 : 0;

            if (headerSize > 0) {
                // Push PreviousFP (= current state.FrameBase as long)
                preBody.Add(Assign(ArrayAccess(ctx.SlotsLocal, callSp),
                    Convert(fbProp, typeof(long))));
                // Push SavedSP (= callSp as long, the SP before the header)
                preBody.Add(Assign(ArrayAccess(ctx.SlotsLocal, Add(callSp, Constant(1))),
                    Convert(callSp, typeof(long))));
                // Advance SP past the 2-word header
                preBody.Add(Call(Property(ctx.State, "Stack"),
                    Ref<ValueStack>.Method(s => s.SetStackPointer(0)),
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
                    Ref<ValueStack>.Method(s => s.SetStackPointer(0)),
                    Add(callSp, Constant(headerSize + invoke.Arguments.Length))));
            }

            // FB = callSp + headerSize + max(args.Length, 1) so params are at slots[FB - N + paramIdx]
            // Even with 0 args, reserve 1 slot for implicit SetArgs parameter.
            int paramCount = Math.Max(1, invoke.Arguments.Length);
            Expression newFb = Add(callSp, Constant(headerSize + paramCount));

            // 6. Set new FrameBase (saved PreviousFP is already in the 2-word header).
            preBody.Add(Assign(fbProp, newFb));
            preBody.Add(Assign(ctx.FrameBaseLocal, newFb)); // sync _fb local

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

            // Restore FrameBase from the 2-word header (PreviousFP at _slots[callSp]).
            // When headerSize == 0 (SetArgs pattern), restore from OldFrameBase legacy field.
            if (headerSize > 0) {
                var prevFp = Convert(ArrayAccess(ctx.SlotsLocal, callSp), typeof(int));
                postBody.Add(Assign(ctx.FrameBaseLocal, prevFp));
                postBody.Add(Assign(fbProp, prevFp));
            }
            else {
                postBody.Add(Assign(ctx.FrameBaseLocal,
                    Property(ctx.State, nameof(VmState.OldFrameBase))));
                postBody.Add(Assign(fbProp,
                    Property(ctx.State, nameof(VmState.OldFrameBase))));
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
        bodyExprs.Add(Assign(fnCtx.FrameBaseLocal, fnCtx.FrameBaseInitExpression));

        if (mode != CompilationMode.NoDebug) {
            fnCtx.DebugHookProp = Property(fnCtx.State, nameof(VmState.DebugHook));
        }

        // Register captures so EmitVariable/EmitAssignment can route to heap reads
        for (int i = 0; i < captures.Count; i++)
            fnCtx.DeclareCapture(captures[i].Variable, i);

        // Declare parameters as value-stack variables (mapped to _slots[_fb + idx])
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

        // Flush result: return value at _slots[_fb], SP = _fb + 1
        if (fnCtx.RingDepth > 0) {
            bodyExprs.Add(Assign(ArrayAccess(fnCtx.SlotsLocal, fnCtx.FrameBaseLocal),
                fnCtx.RingVar(fnCtx.RingDepth - 1)));
            bodyExprs.Add(Assign(fnCtx.SlotsStackPointer,
                Add(fnCtx.FrameBaseLocal, Constant(1))));
        }

        bodyExprs.Add(Label(fnCtx.ExitLabel));

        var delegateExpr = Lambda<Action<VmState>>(Block(fnCtx.Locals, bodyExprs), fnCtx.State);
        return delegateExpr.Compile();
    }

    // ── Helpers ────────────────────────────────────────────────────

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
            ? New(
                typeof(ReadOnlySpan<long>).GetConstructor([typeof(long[])])!,
                NewArrayBounds(typeof(long), Constant(0)))
            : New(
                typeof(ReadOnlySpan<long>).GetConstructor([typeof(long[]), typeof(int), typeof(int)])!,
                ctx.SlotsLocal, ctx.FrameBaseLocal, Constant(localCount));

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

    private static readonly PropertyInfo StateFrameBaseProperty =
        Ref<VmState>.Property(e => e.FrameBase);
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

    // ── Float/double helpers ────────────────────────────────────────

    private static readonly MethodInfo BitConverterInt64BitsToDouble =
        typeof(BitConverter).GetMethod(nameof(BitConverter.Int64BitsToDouble), [typeof(long)])!;

    private static readonly MethodInfo BitConverterDoubleToInt64Bits =
        typeof(BitConverter).GetMethod(nameof(BitConverter.DoubleToInt64Bits), [typeof(double)])!;

    /// <summary>Check if a node is statically known to produce a double/float value.
    /// Returns true when the analysis metadata says the ClrType is double or float.</summary>
    private static bool IsDoubleValue(AbiCtx ctx, Node node) {
        if (ctx.Analysis is null) return false;
        var meta = ctx.Analysis.GetMetadata<ValueRepresentationMetadata>(node);
        if (meta?.ClrType is null) return false;
        return meta.ClrType == typeof(double) || meta.ClrType == typeof(float);
    }

    /// <summary>
    /// Proper recursive expression tree dumper for side-by-side comparison
    /// between direct emitter and primitive path (or for diagnostics).
    /// </summary>
    public static string DumpTree(Expression expr, int indent = 0) {
        var sb = new StringBuilder();
        Dump(expr, sb, indent);
        return sb.ToString();
    }

    private static void Dump(Expression expr, StringBuilder sb, int indent) {
        string pad = new string(' ', indent * 2);
        sb.AppendLine($"{pad}{expr.NodeType} ({expr.Type.Name})");
        if (expr is BinaryExpression b) {
            Dump(b.Left, sb, indent + 1);
            Dump(b.Right, sb, indent + 1);
        }
        else if (expr is UnaryExpression u) {
            if (u.Operand != null) Dump(u.Operand, sb, indent + 1);
        }
        else if (expr is BlockExpression blk) {
            foreach (var e in blk.Expressions) Dump(e, sb, indent + 1);
        }
        else if (expr is ConditionalExpression c) {
            Dump(c.Test, sb, indent + 1);
            Dump(c.IfTrue, sb, indent + 1);
            if (c.IfFalse != null) Dump(c.IfFalse, sb, indent + 1);
        }
        else if (expr is LambdaExpression lam) {
            sb.AppendLine($"{pad}  => ({string.Join(", ", lam.Parameters.Select(p => p.Name + ":" + p.Type.Name))})");
            Dump(lam.Body, sb, indent + 1);
        }
        else if (expr is MethodCallExpression m) {
            if (m.Object != null) Dump(m.Object, sb, indent + 1);
            foreach (var arg in m.Arguments) Dump(arg, sb, indent + 1);
        }
        else if (expr is MemberExpression mem) {
            if (mem.Expression != null) Dump(mem.Expression, sb, indent + 1);
        }
        else if (expr is ConstantExpression) {
            sb.AppendLine($"{pad}  const: {expr.ToString()}");
        }
        // Extend as needed for other node types (Goto, Loop, etc.)
    }

    /// <summary>
    /// ABI Compilation Context — manages ring registers, variable mapping,
    /// and scope tracking for the direct AST-to-ABI emitter.
    ///
    /// RING DISCIPLINE (local, no global allocator):
    /// A stack of "ring slots" (<c>_r0</c>..<c>_rN</c>, each a LINQ
    /// <c>ParameterExpression</c> typed <c>long</c>) represents the
    /// eval stack.  <see cref="RingDepth"/> is the current number of
    /// live values.  Values are placed at successive absolute indices
    /// as they're produced and reclaimed as they're consumed.
    ///
    /// Unlike a global ring allocator, there is no
    /// pre-pass — ring assignment happens inline during the AST walk.
    /// </summary>
    public sealed class AbiCtx {
        private readonly ParameterExpression _stateParam;
        private readonly List<ParameterExpression> _ringVars = new();
        private readonly List<ParameterExpression> _locals = new();

        // ── Construction ─────────────────────────────────────────

        public AbiCtx() {
            _stateParam = Parameter(typeof(VmState), "state");

            ProgramCounter = Variable(typeof(int), "_pc");
            _locals.Add(ProgramCounter);

            SlotsLocal = Variable(typeof(long[]), "_slots");
            _locals.Add(SlotsLocal);

            HeapLocal = Variable(typeof(Heap), "_heap");
            _locals.Add(HeapLocal);

            FrameBaseLocal = Variable(typeof(int), "_fb");
            _locals.Add(FrameBaseLocal);

            SavedSp = Variable(typeof(int), "_savedSp");
            _locals.Add(SavedSp);

            ResultLocal = Variable(typeof(long), "_result");
            _locals.Add(ResultLocal);

            // Initialize the fixed-size register file (user variable cache)
            _regVars = new List<ParameterExpression>(RegisterCount);
            for (int i = 0; i < RegisterCount; i++) {
                var r = Variable(typeof(long), $"_reg{i}");
                _regVars.Add(r);
                _locals.Add(r);
            }

            EntryLabel = Label("_entry");
            ExitLabel = Label("_exit");
        }

        // ── Public state ─────────────────────────────────────────

        public ParameterExpression State => _stateParam;
        public ParameterExpression ProgramCounter { get; }
        public ParameterExpression SlotsLocal { get; }
        public ParameterExpression HeapLocal { get; }
        public ParameterExpression FrameBaseLocal { get; }
        public ParameterExpression SavedSp { get; }
        public ParameterExpression ResultLocal { get; }
        public LabelTarget EntryLabel { get; }
        public LabelTarget ExitLabel { get; }

        /// <summary>Monotonic counter for generating unique label names.</summary>
        public int LabelCounter { get; set; }

        /// <summary>Debug hook callback expression (state.DebugHook), or null in NoDebug mode.
        /// When non-null, the emitter generates local snapshot + hook invocation before each node.</summary>
        public Expression? DebugHookProp { get; set; }

        /// <summary>Expression to read <c>state.CurrentAstNode</c>.</summary>
        public Expression CurrentAstNodeExpr =>
            Property(_stateParam, nameof(VmState.CurrentAstNode));

        /// <summary>
        /// Number of locally declared variables across all active scopes.
        /// Used to size the locals snapshot buffer for the debug hook.
        /// </summary>
        public int CurrentLocalCount {
            get {
                int count = 0;
                foreach (var scope in _scopeStack)
                    count += scope.Count;
                return count;
            }
        }

        /// <summary>Monotonic step counter for DebugInterrupt indexing.
        /// Incremented for each AST node to give stable interrupt points.</summary>
        public int StepCounter { get; set; }

        /// <summary>Expression for <c>state.ProgramCounter</c> — flushed before interrupt.</summary>
        public Expression StatePcFlush => Property(_stateParam, "ProgramCounter");

        /// <summary>Compilation mode for the current emitter context.</summary>
        public CompilationMode Mode { get; set; }

        /// <summary>
        /// Constant expression referencing the compiled function table array
        /// (<c>Action&lt;VmState&gt;[]</c>), or null if no lambdas are present.
        /// </summary>
        public Expression? FunctionTableExpr { get; set; }

        /// <summary>Analysis result from the standard pipeline, for resolving
        /// member access, type information, call sites, etc.</summary>
        public AnalysisResult? Analysis { get; set; }

        /// <summary>All local variables used in the compiled expression tree.</summary>
        public IReadOnlyList<ParameterExpression> Locals => _locals;

        /// <summary>Expression to initialize <c>_slots</c> from <c>state.Stack.RawSlots</c>.</summary>
        public Expression SlotsInitExpression =>
            Property(Property(_stateParam, StateStackProperty), ValueStackRawSlotsProperty);

        /// <summary>Expression to initialize <c>_heap</c> from <c>state.Heap</c>.</summary>
        public Expression HeapInitExpression =>
            Property(_stateParam, "Heap");

        /// <summary>Expression to initialize <c>_fb</c> from <c>state.FrameBase</c>.</summary>
        public Expression FrameBaseInitExpression =>
            Property(_stateParam, StateFrameBaseProperty);

        /// <summary>Expression: <c>state.Registers</c>.</summary>
        public Expression Registers =>
            Property(_stateParam, StateRegistersProperty);

        /// <summary>Expression: <c>state.Heap.RawSlots</c> — the underlying object? array
        /// of the heap, indexed by handle.</summary>
        public Expression HeapRawSlots =>
            Property(HeapLocal, StateHeapRawSlotsProperty);

        /// <summary>Expression: <c>state.ClosureHandle</c>.</summary>
        public Expression ClosureHandle =>
            Property(_stateParam, StateClosureHandleProperty);

        /// <summary>Expression to set the stack pointer (used by return).</summary>
        public Expression SlotsStackPointer =>
            Property(Property(_stateParam, StateStackProperty), ValueStackStackPointerProperty);

        // ── Ring management ──────────────────────────────────────

        /// <summary>Current number of live values on the ring (compile-time depth).</summary>
        public int RingDepth { get; set; }

        private int _maxDepth;

        /// <summary>Peak ring depth observed during compilation.</summary>
        public int MaxRingDepth => _maxDepth;

        /// <summary>Get or create the <c>ParameterExpression</c> for ring slot at absolute index.</summary>
        public ParameterExpression RingVar(int absoluteIndex) {
            while (_ringVars.Count <= absoluteIndex) {
                var v = Variable(typeof(long), $"_r{_ringVars.Count}");
                _ringVars.Add(v);
                _locals.Add(v);
            }
            if (absoluteIndex + 1 > _maxDepth)
                _maxDepth = absoluteIndex + 1;
            return _ringVars[absoluteIndex];
        }

        /// <summary>Allocate a new ring slot (increment depth, ensure var exists).</summary>
        public int AllocSlot() {
            int slot = RingDepth;
            RingDepth = slot + 1;
            RingVar(slot); // ensure it exists
            return slot;
        }

        // ── Variable scope management / Register file ────────────

        private readonly Stack<Dictionary<Variable, int>> _scopeStack = new();
        private readonly Dictionary<Variable, int> _variableRegisters = new(ReferenceEqualityComparer.Instance);
        private readonly Stack<List<Variable>> _scopeVars = new();

        // Fixed-size register file for user variables. The JIT can enregister
        // a small set of locals far more efficiently than per-variable ones or
        // array accesses. 8 registers is generous for any realistic block.
        private const int RegisterCount = 8;
        private readonly List<ParameterExpression> _regVars;
        private readonly bool[] _regUsed = new bool[RegisterCount];

        /// <summary>Enter a new block scope for variable declarations.</summary>
        public void PushScope() {
            _scopeStack.Push(new Dictionary<Variable, int>(ReferenceEqualityComparer.Instance));
            _scopeVars.Push(new List<Variable>());
        }

        /// <summary>Exit the current block scope — emits no expressions;
        /// the caller calls <see cref="EmitScopeStores"/> before this.</summary>
        public void PopScope() {
            // Free registers allocated in this scope
            if (_scopeVars.Count > 0) {
                foreach (var v in _scopeVars.Peek()) {
                    if (_variableRegisters.TryGetValue(v, out int regIdx)) {
                        _regUsed[regIdx] = false;
                        _variableRegisters.Remove(v);
                    }
                }
            }
            _scopeStack.Pop();
            _scopeVars.Pop();
        }

        /// <summary>Store-back expressions for the current scope, flushing register
        /// values back to <c>_slots</c>. Call BEFORE <see cref="PopScope"/>.</summary>
        public IReadOnlyList<Expression> EmitScopeStores() {
            if (_scopeVars.Count == 0 || _scopeStack.Count == 0) return Array.Empty<Expression>();
            var vars = _scopeVars.Peek();
            var scope = _scopeStack.Peek();
            var stores = new List<Expression>(vars.Count);
            foreach (var v in vars) {
                if (_variableRegisters.TryGetValue(v, out int regIdx) && scope.TryGetValue(v, out int slot)) {
                    stores.Add(Assign(
                        ArrayAccess(SlotsLocal, Add(FrameBaseLocal, Constant(slot))),
                        _regVars[regIdx]));
                }
            }
            return stores;
        }

        /// <summary>Load expressions for the current scope, loading register
        /// values from <c>_slots</code> into the register file.</summary>
        public IReadOnlyList<Expression> EmitScopeLoads() {
            if (_scopeVars.Count == 0 || _scopeStack.Count == 0) return Array.Empty<Expression>();
            var vars = _scopeVars.Peek();
            var scope = _scopeStack.Peek();
            var loads = new List<Expression>(vars.Count);
            foreach (var v in vars) {
                if (_variableRegisters.TryGetValue(v, out int regIdx) && scope.TryGetValue(v, out int slot)) {
                    loads.Add(Assign(
                        _regVars[regIdx],
                        ArrayAccess(SlotsLocal, Add(FrameBaseLocal, Constant(slot)))));
                }
            }
            return loads;
        }

        /// <summary>Declare a variable, allocating it to a register file slot.
        /// Writes go through the register; the caller must emit
        /// <see cref="EmitScopeStores"/> before scope exit.</summary>
        public void DeclareVariable(Variable v) {
            if (_scopeStack.Count == 0)
                throw new InvalidOperationException("No active scope");
            int slot = _scopeStack.Peek().Count;
            _scopeStack.Peek()[v] = slot;
            // Allocate a register file slot
            int regIdx = -1;
            for (int i = 0; i < RegisterCount; i++) {
                if (!_regUsed[i]) { regIdx = i; break; }
            }
            if (regIdx < 0) {
                // Out of registers — use _slots directly (fallback; should be rare)
                // We'll fall through to the VariableRead(int) path for this var.
                _scopeVars.Peek().Add(v);
                _variableLayouts.Add(new VariableLayout(v.Name, slot));
                return;
            }
            _regUsed[regIdx] = true;
            _variableRegisters[v] = regIdx;
            _scopeVars.Peek().Add(v);
            _variableLayouts.Add(new VariableLayout(v.Name, slot));
        }

        private readonly List<VariableLayout> _variableLayouts = new();

        /// <summary>Variable layouts collected during lowering for debug info.</summary>
        public IReadOnlyList<VariableLayout> VariableLayouts => _variableLayouts;

        /// <summary>Try to resolve a variable to its value-stack slot index.</summary>
        public bool TryGetVariable(Variable v, out int slot) {
            foreach (var scope in _scopeStack) {
                if (scope.TryGetValue(v, out slot))
                    return true;
            }
            slot = -1;
            return false;
        }

        /// <summary>Number of active function parameters that occupy value stack
        /// slots BEFORE local variables. Parameters live at <c>_slots[_fb - ParamSlotOffset + paramIndex]</c>,
        /// variables live at <c>_slots[_fb + varIndex]</c>.</summary>
        public int ParamSlotOffset { get; set; }

        /// <summary>Expression to read a variable from the value stack: <c>_slots[_fb + varIndex]</c>.
        /// Fallback when no register is available.</summary>
        public Expression VariableRead(int varIndex) =>
            ArrayAccess(SlotsLocal, Add(FrameBaseLocal, Constant(varIndex)));

        /// <summary>Read a variable from its register file slot.
        /// The JIT enregisters this for hot-loop performance.</summary>
        public Expression VariableRead(Variable v) {
            if (_variableRegisters.TryGetValue(v, out int regIdx))
                return _regVars[regIdx];
            if (TryGetVariable(v, out int slotIndex))
                return VariableRead(slotIndex);
            throw new InvalidOperationException($"Variable '{v.Name}' not declared in any scope");
        }

        /// <summary>Expression to write to a variable: <c>_slots[_fb + varIndex] = value</c>.
        /// Fallback when no register is available.</summary>
        public Expression VariableWrite(int varIndex, Expression value) =>
            Assign(VariableRead(varIndex), value);

        /// <summary>Write a variable to its register file slot.
        /// The JIT enregisters this for hot-loop performance.</summary>
        public Expression VariableWrite(Variable v, Expression value) {
            if (_variableRegisters.TryGetValue(v, out int regIdx))
                return Assign(_regVars[regIdx], value);
            if (TryGetVariable(v, out int slotIndex))
                return VariableWrite(slotIndex, value);
            throw new InvalidOperationException($"Variable '{v.Name}' not declared in any scope");
        }

        /// <summary>Read a function parameter from the value stack.
        /// Parameters are stored BEFORE the local variable region:
        /// <c>_slots[_fb - ParamSlotOffset + paramIndex]</c>.</summary>
        public Expression ParameterRead(int paramIndex) =>
            ArrayAccess(SlotsLocal, Add(FrameBaseLocal, Constant(paramIndex - ParamSlotOffset)));

        // ── Loop scope management (for break/continue) ──────────

        private readonly Stack<(LabelTarget breakLabel, LabelTarget continueLabel)> _loopScopes = new();

        /// <summary>Enter a loop scope with the given break and continue labels.</summary>
        public void PushLoopScope(LabelTarget breakLabel, LabelTarget continueLabel) {
            _loopScopes.Push((breakLabel, continueLabel));
        }

        /// <summary>Exit the current loop scope.</summary>
        public void PopLoopScope() => _loopScopes.Pop();

        /// <summary>Get the current loop's break/continue labels, or null if not in a loop.</summary>
        public (LabelTarget breakLabel, LabelTarget continueLabel)? CurrentLoopLabels =>
            _loopScopes.Count > 0 ? _loopScopes.Peek() : null;

        // ── Label management (for goto/label declarations) ──────

        private readonly Dictionary<string, LabelTarget> _labels = new();

        /// <summary>Get or create a label target by name.</summary>
        public LabelTarget GetLabel(string name) {
            if (!_labels.TryGetValue(name, out var target)) {
                target = Label(name);
                _labels[name] = target;
            }
            return target;
        }

        /// <summary>Check if a label has been created.</summary>
        public bool HasLabel(string name) => _labels.ContainsKey(name);

        // ── Parameter management ────────────────────────────────

        private readonly Dictionary<Parameter, int> _parameters = new(ReferenceEqualityComparer.Instance);
        private int _nextParamSlot;

        /// <summary>Register a parameter and assign it the next sequential slot index.</summary>
        public int DeclareParameter(Parameter p) {
            int slot = _nextParamSlot++;
            _parameters[p] = slot;
            return slot;
        }

        /// <summary>Try to resolve a parameter to its slot index.</summary>
        public bool TryGetParameterSlot(Parameter p, out int slot) =>
            _parameters.TryGetValue(p, out slot);

        /// <summary>Save current param slot counter and reset to 0 for a new invocation level.</summary>
        public int SaveAndResetParamSlots() {
            int saved = _nextParamSlot;
            _nextParamSlot = 0;
            return saved;
        }

        /// <summary>Restore the param slot counter from an outer invocation level.</summary>
        public void RestoreParamSlots(int saved) => _nextParamSlot = saved;

        // ── Capture management (upvalues in lambda bodies) ────────

        private readonly Dictionary<Variable, int> _capturedVars = new(ReferenceEqualityComparer.Instance);

        /// <summary>Register a variable as a closure capture with the given capture-array index.</summary>
        public void DeclareCapture(Variable v, int captureIndex) {
            _capturedVars[v] = captureIndex;
        }

        /// <summary>Check if a variable is a capture and get its capture-array index.</summary>
        public bool TryGetCapture(Variable v, out int captureIndex) =>
            _capturedVars.TryGetValue(v, out captureIndex);

        // List of nodes indexed by the step/PC assigned during lowering.
        // This is passed to VmProgram so the debugger can resolve PC -> Node
        // for stack traces, variable name lookup (via the node's scope), etc.
        private readonly List<Node?> _stepNodes = new();
        public IReadOnlyList<Node> StepNodes => _stepNodes.ToArray().Where(n => n is not null).Select(n => n!).ToList().AsReadOnly();

        /// <summary>Record the node for a given step index (used to populate StepNodes for PC->Node debug mapping).</summary>
        public void RecordStepNode(int step, Node node) {
            while (_stepNodes.Count <= step)
                _stepNodes.Add(null);
            _stepNodes[step] = node;
        }

        // ── Compile-time stack / frame simulator (used only while emitting) ──
        //
        // This lets the lowering "simulate" SP adjustments and frame allocation
        // at compile time. Because we know argument/local counts for every
        // scope at lowering time, we can pre-compute exact offsets for every
        // user variable and only emit the minimal runtime operations
        // (the 2-word frame header push + SP advance) at actual call boundaries.
        //
        // Inside a function body the emitted code uses a frameBase local +
        // constant offsets — almost no runtime arithmetic for variable access.

        private readonly Stack<CompileTimeFrame> _ctFrames = new();
        private int _ctSp; // virtual stack pointer, only for layout decisions during emit

        private sealed class CompileTimeFrame {
            public int ArgumentCount { get; }
            public int LocalCount { get; }
            public int BaseOffset { get; }   // offset from this frame's "frame base" where data starts
            public int HeaderSize { get; }   // size of the frame header (0 for current layout, 2 for 2-value model)

            public CompileTimeFrame(int args, int locals, int baseOffset, int headerSize) {
                ArgumentCount = args;
                LocalCount = locals;
                BaseOffset = baseOffset;
                HeaderSize = headerSize;
            }
        }

        /// <summary>
        /// Called during lowering when we cross into a new activation (function entry
        /// or non-inlined call). We "push" a frame in the virtual model.
        /// </summary>
        /// <param name="argumentCount">Number of arguments for this activation.</param>
        /// <param name="localCount">Number of local variables for this activation.</param>
        /// <param name="headerSize">Size of the frame header in words (2 for the full
        /// 2-value frame model; 0 for the current layout where no header is pushed).</param>
        public void EnterActivation(int argumentCount, int localCount, int headerSize = 0) {
            // The data for this frame starts after the header (if any).
            var frame = new CompileTimeFrame(argumentCount, localCount, _ctSp + headerSize, headerSize);
            _ctFrames.Push(frame);
            _ctSp += headerSize + argumentCount + localCount;
        }

        public void LeaveActivation() {
            if (_ctFrames.Count == 0) throw new InvalidOperationException("No active frame");
            var f = _ctFrames.Pop();
            _ctSp -= f.HeaderSize + f.ArgumentCount + f.LocalCount;
        }

        /// <summary>
        /// Returns the compile-time offset for a user variable relative to
        /// the current frame base. The emitted expression will be something like
        /// ArrayAccess(frameBaseLocal, Constant(offset)).
        /// 
        /// Currently returns the scope-relative slot index directly, since _fb
        /// points to the start of the local variable area (no header pushed on
        /// the runtime stack yet). When the 2-word header is added to the
        /// runtime preamble, this method will account for the header size,
        /// argument area, and interleaved linkage values.
        /// </summary>
        public int GetCompileTimeVariableOffset(Variable v) {
            if (!TryGetVariable(v, out int slotInScope))
                throw new InvalidOperationException($"Variable '{v.Name}' has no slot");
            return slotInScope;
        }

        /// <summary>
        /// The exact number of stack slots this frame occupies (known at lowering time).
        /// Used when emitting the "advance SP" part of a frame prologue.
        /// </summary>
        public int GetCurrentFrameSize() =>
            _ctFrames.Count == 0 ? 0 : _ctFrames.Peek().ArgumentCount + _ctFrames.Peek().LocalCount + 2;

        // Note: the *runtime* side of this is the CallStack class (and the
        // actual pushes of the two Word values + SP adjustment that we emit).
    }
}