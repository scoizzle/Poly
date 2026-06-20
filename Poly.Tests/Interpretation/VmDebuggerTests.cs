using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Instructions;

namespace Poly.Tests.Interpretation;

public class VmDebuggerTests {
    [Test]
    public async Task Debugger_BreakpointSet_SyncsToState() {
        var instructions = new Instruction[] { new LoadConst(42), new ReturnOp() };
        var program = ProgramCompiler.Compile(new LoweringResult([.. instructions]));
        var state = new VmState(program);
        var dbg = new VmDebugger(state, program);

        dbg.SetBreakpoint(0);
        await Assert.That(state.Breakpoints).IsNotNull();
        await Assert.That(state.Breakpoints!.Contains(0)).IsTrue();

        dbg.ClearBreakpoint(0);
        await Assert.That(state.Breakpoints).IsNull();
    }

    [Test]
    public async Task Debugger_StepInto_AddsStepBreakpoint() {
        var instructions = new Instruction[] { new LoadConst(42), new ReturnOp() };
        var program = ProgramCompiler.Compile(new LoweringResult([.. instructions]));
        var state = new VmState(program);
        var dbg = new VmDebugger(state, program);

        // Simulate suspension at PC 0
        state.Status = InterpreterStatus.Suspended;
        state.ProgramCounter = 0;

        dbg.StepInto();
        // Step should add breakpoint at PC 1
        await Assert.That(state.Breakpoints).IsNotNull();
        await Assert.That(state.Breakpoints!.Contains(1)).IsTrue();
    }

    [Test]
    public async Task Debugger_Execute_RunsDelegate() {
        var instructions = new Instruction[] { new LoadConst(42), new ReturnOp() };
        var program = ProgramCompiler.Compile(new LoweringResult([.. instructions]));
        var state = new VmState(program);
        var dbg = new VmDebugger(state, program);

        var result = dbg.Execute();
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task Debugger_InspectState() {
        var instructions = new Instruction[] {
            new LoadConst(10), new LoadConst(20), new BinOp(BinOpKind.Add), new ReturnOp()
        };
        var program = ProgramCompiler.Compile(new LoweringResult([.. instructions]));
        var state = new VmState(program);
        var dbg = new VmDebugger(state, program);

        dbg.Execute();
        await Assert.That(dbg.StackHeight).IsEqualTo(1);
        await Assert.That(dbg.PeekStack(0)).IsEqualTo(30L);
        await Assert.That(dbg.GetInstruction(0)).IsTypeOf<LoadConst>();
        await Assert.That(dbg.GetInstruction(2)).IsTypeOf<BinOp>();
        await Assert.That(dbg.GetInstruction(3)).IsTypeOf<ReturnOp>();
    }
}