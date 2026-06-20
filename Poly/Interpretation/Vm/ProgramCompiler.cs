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

        return new VmProgram(del, new Dictionary<NodeId, SourceRange>(), [], null, null, maxActiveLocalDepth);
    }

    private static void ResolveProducers(List<Instruction> instructions) {
        int n = instructions.Count;
        // Entry stacks: snapshot of producer stack at each µop's start
        var entryStacks = new Stack<int>[n];
        // Branch sources: for Jump/BranchIfFalse, record (targetPc, stackAtBranch)
        var branchSaves = new List<(int TargetPc, int SrcPc, int[] Stack)>();

        var producerStack = new Stack<int>();

        for (int pc = 0; pc < n; pc++) {
            var op = instructions[pc];
            // Save entry stack
            entryStacks[pc] = producerStack.Count > 0
                ? new Stack<int>(producerStack.Reverse())
                : new Stack<int>();

            int popCount = op.PopCount;

            // Record branch sources (stack BEFORE consumption)
            if (op is Jump jmp) {
                branchSaves.Add((jmp.Target, pc, producerStack.Reverse().ToArray()));
            }
            else if (op is BranchIfFalse bif) {
                branchSaves.Add((bif.Target, pc, producerStack.Reverse().ToArray()));
                if (pc + 1 < n)
                    branchSaves.Add((pc + 1, pc, producerStack.Reverse().ToArray()));
            }

            // Consume
            var consumed = new int[popCount];
            for (int i = popCount - 1; i >= 0 && producerStack.Count > 0; i--)
                consumed[i] = producerStack.Pop();
            if (popCount > 0)
                instructions[pc] = op with { ConsumedFromPcs = consumed };

            // Produce
            for (int push = 0; push < op.PushCount; push++)
                producerStack.Push(pc);
        }

        // Pass 2: at each µop that has a branch source targeting it,
        // compare the linear entry stack with the branch source stack.
        // Where they differ after consuming PopCount values, set φ.
        var targetsByPc = branchSaves.GroupBy(s => s.TargetPc)
            .ToDictionary(g => g.Key, g => g.ToList());

        for (int pc = 0; pc < n; pc++) {
            if (!targetsByPc.TryGetValue(pc, out var incoming)) continue;
            var op = instructions[pc];
            int popCount = op.PopCount;
            if (popCount == 0 || op.ConsumedFromPcs is null) continue;

            var linearStack = entryStacks[pc];
            var primary = op.ConsumedFromPcs;

            // Check saved stacks in reverse order (most recent branch first).
            // Use the LAST differing stack for φ — it represents the most
            // specific predecessor.
            (int SrcPc, int[] Stack)? best = null;
            for (int j = incoming.Count - 1; j >= 0; j--) {
                var (_, srcPc, stackArr) = incoming[j];
                var altStack = new Stack<int>(stackArr);
                var alt = new int[popCount];
                bool consistent = true;
                for (int i = popCount - 1; i >= 0; i--) {
                    int val = altStack.Count > 0 ? altStack.Pop() : 0;
                    if (val != primary[i]) consistent = false;
                    alt[i] = val;
                }
                if (!consistent) { best = (srcPc, alt); break; }
            }
            if (best is { } phi) {
                instructions[pc] = op with {
                    ConsumedFromPcs = primary,
                    PhiSourcePcs = Enumerable.Repeat(phi.SrcPc, popCount).ToArray(),
                    PhiAltPcs = phi.Stack
                };
            }
        }
    }
}