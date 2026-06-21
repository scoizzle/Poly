using System.Linq.Expressions;

using Poly.Interpretation.Vm.Instructions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

public enum CompilationMode { NoDebug, Profiling, Normal }

public static class ProgramCompiler {
    public static VmProgram Compile(LoweringResult input, int maxActiveLocalDepth = 32, CompilationMode mode = CompilationMode.Normal) {
        // Normal mode (default): loop limits enabled.
        // Use CompilationMode.NoDebug to disable safety features for benchmarks.
        var instructions = input.Instructions;
        // When the new lowering-prep pipeline already set ConsumedFromPcs, skip.
        // For raw µop lists (tests, direct API callers), compute via backward scan.
        bool alreadyResolved = false;
        for (int i = 0; i < instructions.Count; i++)
            if (instructions[i].PopCount > 0 && instructions[i].ConsumedFromPcs is not null) { alreadyResolved = true; break; }
        if (!alreadyResolved)
            BackwardScan(instructions);

        // Use the depth computed during lowering if provided.
        int maxDepth = Math.Max(maxActiveLocalDepth, input.MaxActiveLocalsDepth);

        var ctx = new CompilationContext();
        var body = new List<Expression>();
        int n = instructions.Count;

        ctx.LimitLoops = mode is CompilationMode.Normal or CompilationMode.Profiling;

        for (int i = 0; i < n; i++) {
            ctx.GetLabel(i);
            ctx.ValueSlot(i);
        }

        body.Add(Label(ctx.EntryLabel));

        // Initialize Registers lazily so callers don't need to set it manually.
        // Preamble: runs before any µop, regardless of entry path.
        body.Add(Assign(ctx.Registers,
            Coalesce(ctx.Registers, NewArrayBounds(typeof(long), Constant(maxDepth)))));
        // Initialize FrameBase to 0 for top-level execution (FrameBase = -1
        // causes LoadSlot/StoreSlot to access RawSlots[-1], which is OOB).
        body.Add(IfThen(
            Equal(Property(ctx.State, "FrameBase"), Constant(-1)),
            Assign(Property(ctx.State, "FrameBase"), Constant(0))));
        if (mode == CompilationMode.Profiling) {
            body.Add(Assign(ctx.InstructionCounters, NewArrayBounds(typeof(long), Constant(n))));
        }
        if (ctx.LimitLoops) {
            // Cache MaxLoopIterations in a local and compute active flag once.
            var maxIterProp = Property(ctx.State, nameof(VmState.MaxLoopIterations));
            body.Add(Assign(ctx.LoopMaxIter, maxIterProp));
            body.Add(Assign(ctx.LoopLimitActive,
                NotEqual(ctx.LoopMaxIter, Constant(-1L))));
            // Lazy-init LoopCounters when loop limiting is active.
            body.Add(IfThen(
                AndAlso(
                    ctx.LoopLimitActive,
                    Equal(Property(ctx.State, nameof(VmState.LoopCounters)), Constant(null, typeof(long[])))),
                Assign(Property(ctx.State, nameof(VmState.LoopCounters)),
                    NewArrayBounds(typeof(long), Constant(n)))));
        }

        if (n > 0) {
            var switchCases = new System.Linq.Expressions.SwitchCase[n];
            for (int i = 0; i < n; i++)
                switchCases[i] = SwitchCase(Goto(ctx.GetLabel(i)), Constant(i));
            body.Add(IfThen(
                test: GreaterThanOrEqual(ctx.ProgramCounter, Constant(0)),
                ifTrue: Switch(ctx.ProgramCounter, Goto(ctx.ExitLabel), switchCases)));
        }

        for (int pc = 0; pc < n; pc++) {
            var op = instructions[pc];
            ctx.CurrentLabelIndex = pc;

            body.Add(Label(ctx.GetLabel(pc)));

            if (mode == CompilationMode.Profiling) {
                body.Add(PreIncrementAssign(ArrayAccess(ctx.InstructionCounters, Constant(pc))));
            }

            var result = op.ToExpression(ctx);
            if (result is not null) {
                // Set ProgramCounter before branches so φ can identify the
                // predecessor path at convergence points.  Only φ-relevant
                // jumps need this; a future optimization could skip the
                // write for loop back-edges (detected by checking whether
                // any instruction's PhiSourcePcs references this PC).
                if (op is Jump or BranchIfFalse)
                    body.Add(Assign(ctx.ProgramCounter, Constant(pc)));
                body.Add(result);
            }
        }

        body.Add(Label(ctx.ExitLabel));

        var delegateExpr = Lambda<Action<VmState>>(Block(ctx.Locals, body), ctx.State);
        var del = delegateExpr.Compile();

        return new VmProgram(del, instructions, new Dictionary<NodeId, SourceRange>(), [], null, null, maxDepth);
    }

    /// <summary>Compute ConsumedFromPcs for raw µop lists via linear backward scan.
    /// Callers that need φ at merge points should set ConsumedFromPcs, PhiSourcePcs,
    /// and PhiAltPcs manually.</summary>
    private static void BackwardScan(List<Instruction> instructions) {
        var ring = new List<int>();
        for (int pc = 0; pc < instructions.Count; pc++) {
            var op = instructions[pc];
            var consumed = new int[op.PopCount];
            int entryDepth = ring.Count;
            int toPop = Math.Min(op.PopCount, entryDepth);
            for (int i = 0; i < toPop; i++)
                consumed[op.PopCount - 1 - i] = ring[entryDepth - 1 - i];

            if (op.PopCount > 0)
                instructions[pc] = op with { ConsumedFromPcs = consumed };

            for (int i = 0; i < toPop && ring.Count > 0; i++)
                ring.RemoveAt(ring.Count - 1);
            for (int i = 0; i < op.PushCount; i++)
                ring.Add(pc);
        }
    }
}