using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.Vm.Instructions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

public enum CompilationMode { NoDebug, Debug, Normal, Profiling, TraceExpressions }

public static class ProgramCompiler {
    public static VmProgram Compile(LoweringResult input, int maxActiveLocalDepth = 32, CompilationMode mode = CompilationMode.Normal) {
        // Normal mode (default): loop limits enabled.
        // Use CompilationMode.NoDebug to disable safety features for benchmarks.
        var instructions = input.Instructions;

        // Heap constants are pre-collected during UopGeneration and carried
        // through the LoweringResult.  No instruction patching needed —
        // LoadHeapConst already has the correct handle at emission time.
        var heapConstants = input.HeapConstants;

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

        for (int i = 0; i < n; i++)
            ctx.GetLabel(i);

        // Ring-based allocation: compute µop value depths and create _r{k} pool.
        var ringDepthMap = ComputeRingDepths(instructions, out var ringDepthAtPC);
        ctx.ConfigureRingAllocation(ringDepthMap, maxActiveLocalDepth, input.MaxActiveLocalsDepth);
        ctx.SetRingDepthMap(ringDepthAtPC);

        body.Add(Label(ctx.EntryLabel));

        // Cache property references in locals so µops don't re-fetch them.
        body.Add(Assign(ctx.SlotsLocal, ctx.SlotsInitExpression));
        body.Add(Assign(ctx.HeapLocal, ctx.HeapInitExpression));
        // Initialize Registers lazily so callers don't need to set it manually.
        // Preamble: runs before any µop, regardless of entry path.
        body.Add(Assign(ctx.Registers,
            Coalesce(ctx.Registers, NewArrayBounds(typeof(long), Constant(maxDepth)))));
        // Initialize FrameBase to 0 for top-level execution (FrameBase = -1
        // causes LoadSlot/StoreSlot to access RawSlots[-1], which is OOB).
        body.Add(IfThen(
            Equal(Property(ctx.State, "FrameBase"), Constant(-1)),
            Assign(Property(ctx.State, "FrameBase"), Constant(0))));
        // Cache FrameBase in a local so LoadSlot/StoreSlot don't re-read the property.
        body.Add(Assign(ctx.FrameBaseLocal, ctx.FrameBaseInitExpression));

        if (mode != CompilationMode.NoDebug) {
            // Sync _pc from state.ProgramCounter for dispatch, call return,
            // and breakpoint resume.
            body.Add(Assign(ctx.ProgramCounter, ctx.StateProgramCounter));

            // Restore ring registers when resuming from breakpoint suspension.
            if (ctx.RingRegisterCount > 0) {
                var savedDepth = Property(ctx.State, nameof(VmState.SavedRingDepth));
                var restoreStmts = new List<Expression>(ctx.RingRegisterCount + 1);
                for (int k = 0; k < ctx.RingRegisterCount; k++)
                    restoreStmts.Add(IfThen(
                        GreaterThan(savedDepth, Constant(k)),
                        Assign(ctx.RingSlot(k), ArrayAccess(ctx.Registers, Constant(k)))));
                restoreStmts.Add(
                    Assign(Property(ctx.State, nameof(VmState.NeedsRingRestore)), Constant(false)));
                body.Add(IfThen(
                    Property(ctx.State, nameof(VmState.NeedsRingRestore)),
                    Block(restoreStmts)));
            }
        }
        else {
            // Initialize _pc to 0 for fresh start.  ReturnFromCall / Call
            // µops set _pc explicitly after return, so the dispatch switch
            // still routes correctly.
            body.Add(Assign(ctx.ProgramCounter, Constant(0)));
        }

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

            if (mode == CompilationMode.Debug) {
                var interrupt = Property(ctx.State, nameof(VmState.DebugInterrupt));
                var skipResume = Property(ctx.State, nameof(VmState.SuspendResume));
                var status = Property(ctx.State, nameof(VmState.Status));
                var spill = Instructions.Call.CtxPushRegisters(ctx);
                int depth = ctx.GetRingDepth(pc);

                body.Add(IfThen(
                    AndAlso(
                        NotEqual(interrupt, Constant(null, typeof(Action<VmState>))),
                        Not(skipResume)),
                    Block(
                        Invoke(interrupt, ctx.State),
                        IfThen(Equal(status, Constant(InterpreterStatus.Suspended)),
                            Block(spill,
                                Assign(Property(ctx.State, "ProgramCounter"), Constant(pc)),
                                Assign(Property(ctx.State, nameof(VmState.SuspendResume)), Constant(true)),
                                Assign(Property(ctx.State, nameof(VmState.SavedRingDepth)), Constant(depth)),
                                Assign(Property(ctx.State, nameof(VmState.NeedsRingRestore)), Constant(true)),
                                Goto(ctx.ExitLabel))))));
                body.Add(Assign(skipResume, Constant(false)));
            }

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

        if (mode == CompilationMode.TraceExpressions) {
            var dbgView = typeof(Expression)
                .GetProperty("DebugView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(delegateExpr) as string;
            Console.Error.WriteLine("// ── Compiled µop expression tree ──");
            Console.Error.WriteLine(dbgView ?? delegateExpr.ToString());
            Console.Error.WriteLine("// ── End expression tree ──");
        }

        var del = delegateExpr.Compile();

        return new VmProgram(del, instructions, new Dictionary<NodeId, SourceRange>(), [], heapConstants, null, maxDepth);
    }

    /// <summary>Simulate the eval-stack ring to compute each producer µop's ring depth.
    /// Returns a map: producer PC → ring index (<c>_r{index}</c>).
    /// Also outputs a map: PC → ring depth (eval-stack item count) at each µop.</summary>
    private static Dictionary<int, int> ComputeRingDepths(List<Instruction> instructions, out Dictionary<int, int> ringDepthAtPC) {
        var ring = new List<int>();
        var map = new Dictionary<int, int>();
        ringDepthAtPC = new Dictionary<int, int>();
        for (int pc = 0; pc < instructions.Count; pc++) {
            var op = instructions[pc];
            int entryDepth = ring.Count;
            ringDepthAtPC[pc] = entryDepth;
            int toPop = Math.Min(op.PopCount, entryDepth);
            for (int i = 0; i < toPop && ring.Count > 0; i++)
                ring.RemoveAt(ring.Count - 1);
            if (op.PushCount > 0) {
                map[pc] = entryDepth - toPop;
                ring.Add(pc);
            }
        }
        return map;
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