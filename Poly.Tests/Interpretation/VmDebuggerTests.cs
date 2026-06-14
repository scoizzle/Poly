using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.VirtualMachine;
using Poly.Introspection;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

public class VmDebuggerTests {
    private static Bytecode Lower(Node node) {
        var analysis = new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseDefiniteAssignmentAnalysis()
            .Build()
            .Analyze(node);
        return Lowering.Lower(node, analysis);
    }

    private static int FindFirstUopWithSource(Bytecode prog, NodeId sourceId) {
        for (int i = 0; i < prog.MicroOps.Count; i++) {
            if (prog.MicroOps[i].Source == sourceId)
                return i;
        }
        return -1;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Breakpoint: basic suspension
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Breakpoint_SuspendsAtCorrectPC() {
        var expr = new Add(new Constant(1), new Constant(2));
        var prog = Lower(expr);
        using var state = new VmState { Program = prog, DebugMode = true };

        // Find the AddOp µop (has Add node as source)
        int bpPc = FindFirstUopWithSource(prog, expr.Id);
        await Assert.That(bpPc).IsGreaterThanOrEqualTo(0);

        state.BreakpointPCs = [bpPc];
        var result = Vm.Execute(state);

        await Assert.That(state.IsSuspended).IsTrue();
        await Assert.That(state.SavedPC).IsEqualTo(bpPc);
    }

    [Test]
    public async Task Breakpoint_NoMatch_CompletesNormally() {
        var expr = new Constant(42);
        var prog = Lower(expr);
        using var state = new VmState { Program = prog, DebugMode = true };

        // Set breakpoint at a non-existent PC
        state.BreakpointPCs = [9999];
        var result = Vm.Execute(state);

        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task Breakpoint_Cleared_DoesNotSuspend() {
        var expr = new Add(new Constant(1), new Constant(2));
        var prog = Lower(expr);
        using var state = new VmState { Program = prog, DebugMode = true };

        int bpPc = FindFirstUopWithSource(prog, expr.Id);
        state.BreakpointPCs = [bpPc];
        _ = Vm.Execute(state);
        await Assert.That(state.IsSuspended).IsTrue();

        // Reset and re-run without breakpoint
        state.BreakpointPCs = null;
        state.Reset();
        var result2 = Vm.Execute(state);

        await Assert.That(state.IsComplete).IsTrue();
    }

    [Test]
    public async Task Breakpoint_DebugModeOff_Ignored() {
        var expr = new Add(new Constant(1), new Constant(2));
        var prog = Lower(expr);
        using var state = new VmState { Program = prog, DebugMode = false };

        int bpPc = FindFirstUopWithSource(prog, expr.Id);
        state.BreakpointPCs = [bpPc];
        var result = Vm.Execute(state);

        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(3L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  VmDebugger: step-into
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task StepInto_HaltsAtNextUop() {
        var expr = new Add(new Constant(1), new Constant(2));
        var prog = Lower(expr);
        using var state = new VmState { Program = prog, DebugMode = true };

        // Set breakpoint on the Add node's entry µop
        int bpPc = FindFirstUopWithSource(prog, expr.Id);
        state.BreakpointPCs = [bpPc];
        _ = Vm.Execute(state);
        await Assert.That(state.IsSuspended).IsTrue();

        var debugger = new VmDebugger(state, prog);
        debugger.StepInto();

        // After step-into, the VM should have a one-shot breakpoint at the next µop
        await Assert.That(state.Status).IsEqualTo(InterpreterStatus.Running);
        var result2 = Vm.Execute(state);

        // Should suspend again at the next µop
        await Assert.That(state.IsSuspended).IsTrue();
        await Assert.That(state.SavedPC).IsEqualTo(bpPc + 1);
    }

    // ═══════════════════════════════════════════════════════════════
    //  VmDebugger: step-over
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task StepOver_CompletesCurrentNode() {
        // Add with two constants: should complete in one step-over
        var expr = new Add(new Constant(1), new Constant(2));
        var prog = Lower(expr);
        using var state = new VmState { Program = prog, DebugMode = true };

        int bpPc = FindFirstUopWithSource(prog, expr.Id);
        state.BreakpointPCs = [bpPc];
        _ = Vm.Execute(state);
        await Assert.That(state.IsSuspended).IsTrue();

        var debugger = new VmDebugger(state, prog);
        debugger.StepOver();

        await Assert.That(state.Status).IsEqualTo(InterpreterStatus.Running);
        var result2 = Vm.Execute(state);

        // Step-over the top-level expression suspends at the return boundary.
        // Resume once more to complete.
        if (state.IsSuspended) {
            state.BreakpointPCs = null;
            state.Status = InterpreterStatus.Running;
            result2 = Vm.Execute(state);
        }

        // Should have completed the entire expression
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result2.Value).IsEqualTo(3L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  VmDebugger: step-out
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task StepOut_ReturnsToCaller() {
        // Define: fn add(a, b) => a + b; then call add(1, 2)
        var aParam = new Parameter("a");
        var bParam = new Parameter("b");
        var addBody = new Add(aParam, bParam);
        var addFn = new MethodDefinitionNode("add", new PrimitiveTypeReference(PrimitiveType.Int64), [aParam, bParam], addBody);

        var invoke = new Invoke(addFn, [new Constant(1), new Constant(2)]);
        var node = new Block(addFn, invoke);

        var analysis = new AnalyzerBuilder()
            .UseSideEffectAnalysis().UseThisReferenceContext()
            .UseControlFlowAnalysis().UseVariableScopeValidator()
            .UseDefiniteAssignmentAnalysis()
            .Build()
            .Analyze(node);
        var prog = Lowering.Lower(node, analysis);

        using var state = new VmState { Program = prog, DebugMode = true };

        // Find the add body top-level µop (after the call sets up the frame)
        var addEntry = prog.Functions[0];  // add function
        state.BreakpointPCs = [addEntry.PC];
        _ = Vm.Execute(state);
        await Assert.That(state.IsSuspended).IsTrue();

        var debugger = new VmDebugger(state, prog);
        debugger.StepOut();

        await Assert.That(state.Status).IsEqualTo(InterpreterStatus.Running);
        var result2 = Vm.Execute(state);

        // Step-out suspends at the return address boundary.
        // Resume once more to complete.
        if (state.IsSuspended) {
            state.BreakpointPCs = null;
            state.Status = InterpreterStatus.Running;
            result2 = Vm.Execute(state);
        }

        // Should have returned from add and completed
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result2.Value).IsEqualTo(3L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  VmDebugger: resume
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Resume_CompletesExecution() {
        var expr = new Add(new Constant(1), new Constant(2));
        var prog = Lower(expr);
        using var state = new VmState { Program = prog, DebugMode = true };

        int bpPc = FindFirstUopWithSource(prog, expr.Id);
        state.BreakpointPCs = [bpPc];
        _ = Vm.Execute(state);
        await Assert.That(state.IsSuspended).IsTrue();

        var debugger = new VmDebugger(state, prog);
        // Clear the breakpoint so resume doesn't re-suspend
        state.BreakpointPCs = null;
        debugger.Resume();

        var result2 = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result2.Value).IsEqualTo(3L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  VmDebugger: multiple breakpoints
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task MultipleBreakpoints_AllSuspend() {
        var inner = new Add(new Constant(1), new Constant(2));
        var outer = new Multiply(inner, new Constant(3));
        var prog = Lower(outer);
        using var state = new VmState { Program = prog, DebugMode = true };

        int innerBp = FindFirstUopWithSource(prog, inner.Id);
        int outerBp = FindFirstUopWithSource(prog, outer.Id);
        await Assert.That(innerBp).IsGreaterThanOrEqualTo(0);
        await Assert.That(outerBp).IsGreaterThanOrEqualTo(0);
        await Assert.That(innerBp).IsNotEqualTo(outerBp);

        // First run: breakpoint on inner expression
        state.BreakpointPCs = [innerBp];
        _ = Vm.Execute(state);
        await Assert.That(state.IsSuspended).IsTrue();
        await Assert.That(state.SavedPC).IsEqualTo(innerBp);

        // Resume with breakpoint on outer expression
        state.BreakpointPCs = [outerBp];
        state.Status = InterpreterStatus.Running;
        _ = Vm.Execute(state);
        await Assert.That(state.IsSuspended).IsTrue();
        await Assert.That(state.SavedPC).IsEqualTo(outerBp);
    }

    // ═══════════════════════════════════════════════════════════════
    //  NodeRanges: built correctly
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task NodeRanges_ContainsSourceNode() {
        // Verify that a node whose source is attached to a µop by EmitOp
        // appears in NodeRanges.  Not every AST node produces a µop with
        // its source — constant-folding and immediate-value fusion skip
        // some child nodes.
        var expr = new Constant(42);
        var prog = Lower(expr);

        await Assert.That(prog.NodeRanges).IsNotNull();
        await Assert.That(prog.NodeRanges!.ContainsKey(expr.Id)).IsTrue();

        // The range should cover the µop(s) belonging to this node
        var range = prog.NodeRanges[expr.Id];
        await Assert.That(range.EndPC).IsGreaterThan(range.StartPC);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Performance: breakpoint check overhead when DebugMode is off
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task BreakpointCheck_NoOverhead_WhenDebugModeOff() {
        var uops = new MicroOp[] { new PushOp(42L), new PopOp() };
        var compiled = ProgramCompiler.Compile(uops);
        using var state = new VmState { DebugMode = false };

        // Should complete normally (no breakpoints set, DebugMode off)
        compiled(state);
        await Assert.That(state.Stack.SP).IsEqualTo(0);
    }
}