using System.Linq.Expressions;

using Poly.Interpretation.Vm.Instructions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

public enum CompilationMode { Normal, Profiling, Debug }

public static class ProgramCompiler {
    public static VmProgram Compile(LoweringResult input, int maxActiveLocalDepth = 32, CompilationMode mode = CompilationMode.Normal) {
        var instructions = input.Instructions;
        ResolveProducers(instructions);

        var ctx = new CompilationContext();
        var body = new List<Expression>();
        int n = instructions.Count;

        for (int i = 0; i < n; i++) {
            ctx.GetLabel(i);
            ctx.ValueSlot(i);
        }

        body.Add(Label(ctx.EntryLabel));

        // Initialize Registers lazily so callers don't need to set it manually.
        // Preamble: runs before any µop, regardless of entry path.
        body.Add(Assign(ctx.Registers,
            Coalesce(ctx.Registers, NewArrayBounds(typeof(long), Constant(maxActiveLocalDepth)))));
        // Initialize FrameBase to 0 for top-level execution (FrameBase = -1
        // causes LoadSlot/StoreSlot to access RawSlots[-1], which is OOB).
        body.Add(IfThen(
            Equal(Property(ctx.State, "FrameBase"), Constant(-1)),
            Assign(Property(ctx.State, "FrameBase"), Constant(0))));
        if (mode == CompilationMode.Profiling) {
            body.Add(Assign(ctx.InstructionCounters, NewArrayBounds(typeof(long), Constant(n))));
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
                // predecessor path at convergence points.
                if (op is Jump or BranchIfFalse)
                    body.Add(Assign(ctx.ProgramCounter, Constant(pc)));
                body.Add(result);
            }
        }

        body.Add(Label(ctx.ExitLabel));

        var delegateExpr = Lambda<Action<VmState>>(Block(ctx.Locals, body), ctx.State);
        var del = delegateExpr.Compile();

        return new VmProgram(del, instructions, new Dictionary<NodeId, SourceRange>(), [], null, null, maxActiveLocalDepth);
    }

    private static void ResolveProducers(List<Instruction> instructions) {
        int n = instructions.Count;

        // Step 1: Build predecessor graph
        var predecessors = new List<int>[n];
        for (int i = 0; i < n; i++) predecessors[i] = [];

        for (int pc = 0; pc < n; pc++) {
            var op = instructions[pc];
            if (pc + 1 < n && op is not (Jump or ReturnOp or ReturnFromCall))
                predecessors[pc + 1].Add(pc);
            if (op is Jump jmp && jmp.Target >= 0 && jmp.Target < n)
                predecessors[jmp.Target].Add(pc);
            if (op is BranchIfFalse bif) {
                if (bif.Target >= 0 && bif.Target < n)
                    predecessors[bif.Target].Add(pc);
                if (pc + 1 < n && !predecessors[pc + 1].Contains(pc))
                    predecessors[pc + 1].Add(pc);
            }
        }

        // Step 2: For each µop, compute exit stacks per predecessor.
        // exitStacks[predPc] = stack after processing µop at predPc
        var exitStacks = new int[n][];

        for (int pc = 0; pc < n; pc++) {
            var op = instructions[pc];
            var preds = predecessors[pc];
            int popCount = op.PopCount;
            int pushCount = op.PushCount;

            int[]? entryStack = null;

            if (preds.Count == 0) {
                // No predecessors — entry point
                entryStack = [];
            }
            else {
                // Use the last predecessor's exit stack (fallthrough path).
                // When the last predecessor is a back-edge Jump with null exit
                // stack (during the linear pass), fall back to the first
                // predecessor which will be available (the fallthrough entry).
                entryStack = exitStacks[preds[^1]] ?? (preds.Count >= 2 ? exitStacks[preds[0]] : null) ?? [];
            }

            // Consume values from the entry stack
            var consumed = new int[popCount];
            int entryDepth = entryStack.Length;
            int toPop = Math.Min(popCount, entryDepth);
            for (int i = 0; i < toPop; i++)
                consumed[popCount - 1 - i] = entryStack[entryDepth - 1 - i];

            // φ detection: compare consumed values across all predecessors
            if (popCount > 0 && preds.Count >= 2) {
                // Check each predecessor's entry stack
                bool needsPhi = false;
                int phiSrcPc = -1;
                int[]? phiAlt = null;

                foreach (var predPc in preds) {
                    var predExit = exitStacks[predPc];
                    if (predExit is null) continue;

                    int predDepth = predExit.Length;
                    int predToPop = Math.Min(popCount, predDepth);
                    var predConsumed = new int[popCount];
                    for (int i = 0; i < predToPop; i++)
                        predConsumed[popCount - 1 - i] = predExit[predDepth - 1 - i];

                    bool differs = false;
                    for (int i = 0; i < popCount; i++)
                        if (consumed[i] != predConsumed[i]) { differs = true; break; }

                    if (differs) {
                        needsPhi = true;
                        phiSrcPc = predPc;
                        phiAlt = predConsumed;
                        break;
                    }
                }

                if (needsPhi && phiAlt is not null) {
                    instructions[pc] = op with {
                        ConsumedFromPcs = consumed,
                        PhiSourcePcs = Enumerable.Repeat(phiSrcPc, popCount).ToArray(),
                        PhiAltPcs = phiAlt
                    };
                }
                else {
                    instructions[pc] = op with { ConsumedFromPcs = consumed };
                }
            }
            else if (popCount > 0) {
                instructions[pc] = op with { ConsumedFromPcs = consumed };
            }

            // Compute exit stack: entry stack without consumed values, plus new producers
            int copyCount = Math.Max(0, entryDepth - popCount);
            int newDepth = copyCount + pushCount;
            var exit = new int[newDepth];
            for (int i = 0; i < copyCount; i++)
                exit[i] = entryStack[i];
            for (int i = 0; i < pushCount; i++)
                exit[copyCount + i] = pc;

            exitStacks[pc] = exit;
        }
    }
}