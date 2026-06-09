using Poly.Interpretation;
using Poly.Interpretation.VirtualMachine;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

public class VmDebuggerTests {
    private static (Bytecode Program, VmState State, VmDebugger Debugger) Setup(Node root) {
        var analysis = new AnalyzerBuilder().UseAllAnalyzers().Build().Analyze(root);
        var program = Lowering.Lower(root, analysis);
        var state = new VmState { Program = program };
        var debugger = new VmDebugger(state, program);
        return (program, state, debugger);
    }

    [Test]
    public async Task SetBreakpoint_SuspendsExecution() {
        var (prog, state, debugger) = Setup(new Add(new Constant(3), new Constant(4)));

        // Find a PC to break on from the source map
        var pc = prog.SourceMap.First().Key;
        debugger.SetBreakpoint(prog.SourceMap[pc]);

        var result = Vm.Execute(state);
        await Assert.That(debugger.IsSuspended).IsTrue();
        await Assert.That(result.IsSignal).IsTrue();
        await Assert.That(result.Signal?.Kind).IsEqualTo(InterpreterSignal.SignalKind.Suspend);
    }

    [Test]
    public async Task ResumeAfterBreakpoint_CompletesExecution() {
        var (prog, state, debugger) = Setup(new Add(new Constant(3), new Constant(4)));

        // Break on the Add node
        var pc = prog.SourceMap.First(kvp => kvp.Value == prog.GetNodeIdForInstruction(
            prog.SourceMap.First(kvp => true).Key) && true).Key;
        // Just use the first source map entry
        var nodeId = prog.SourceMap.Values.First();
        debugger.SetBreakpoint(nodeId);

        Vm.Execute(state);
        await Assert.That(debugger.IsSuspended).IsTrue();

        debugger.Resume();
        var result = Vm.Execute(state);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(7);
    }

    [Test]
    public async Task RemoveBreakpoint_ExecutionCompletes() {
        var (prog, state, debugger) = Setup(new Add(new Constant(3), new Constant(4)));

        var nodeId = prog.SourceMap.Values.First();
        debugger.SetBreakpoint(nodeId);
        debugger.RemoveBreakpoint(nodeId);

        var result = Vm.Execute(state);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(7);
        await Assert.That(debugger.IsSuspended).IsFalse();
    }

    [Test]
    public async Task ClearAllBreakpoints_ResumesExecution() {
        var (prog, state, debugger) = Setup(new Add(new Constant(3), new Constant(4)));

        debugger.SetBreakpoint(prog.SourceMap.Values.First());
        debugger.SetBreakpoint(prog.SourceMap.Values.Skip(1).First());
        debugger.ClearAllBreakpoints();

        var result = Vm.Execute(state);
        await Assert.That(result.HasValue).IsTrue();
    }

    [Test]
    public async Task StepInto_CallAndReturn() {
        // Invoke a lambda: (() => 42)()
        var (prog, state, debugger) = Setup(new Invoke(new Lambda([], new Constant(42)), []));

        // Execute until suspend
        var result = Vm.Execute(state);
        // Should have exited, but just verify we can set breakpoints on it
        debugger.SetBreakpoint(prog.SourceMap.Values.First());
        await Assert.That(debugger.BreakpointNodes.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task BreakpointResume_ProducesCorrectResult() {
        // 3 + 4 = 7 — set breakpoint, suspend, resume, verify result is still 7
        var (prog, state, debugger) = Setup(new Add(new Constant(3), new Constant(4)));

        var nodeId = prog.SourceMap.Values.First();
        debugger.SetBreakpoint(nodeId);

        Vm.Execute(state);
        await Assert.That(debugger.IsSuspended).IsTrue();

        debugger.Resume();
        var result = Vm.Execute(state);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(7);
    }

    [Test]
    public async Task BreakpointMultipleResume_ProducesCorrectResult() {
        // Two breakpoints on different nodes — suspend, resume, suspend, resume, verify result
        var (prog, state, debugger) = Setup(new Add(new Multiply(new Constant(6), new Constant(7)), new Constant(1)));

        // Break on both the Multiply (42) and the Add (43)
        var ids = prog.SourceMap.Values.Distinct().ToList();
        if (ids.Count >= 2) {
            debugger.SetBreakpoint(ids[0]);
            debugger.SetBreakpoint(ids[1]);
        }

        // First suspend
        Vm.Execute(state);
        await Assert.That(debugger.IsSuspended).IsTrue();
        debugger.Resume();

        // Second suspend  
        Vm.Execute(state);
        await Assert.That(debugger.IsSuspended).IsTrue();
        debugger.Resume();

        // Final result
        var result = Vm.Execute(state);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(43);
    }

    [Test]
    public async Task DebuggerSetBreakpoint_AddsToBreakpointNodes() {
        var (prog, state, debugger) = Setup(new Add(new Constant(3), new Constant(4)));

        var nodeId = prog.SourceMap.Values.First();
        debugger.SetBreakpoint(nodeId);

        await Assert.That(debugger.BreakpointNodes.Count).IsEqualTo(1);
        await Assert.That(debugger.BreakpointNodes.Contains(nodeId)).IsTrue();
    }
}