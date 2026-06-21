using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Instructions;

namespace Poly.Tests.Interpretation;

public class VmDebuggerTests {
    private static (VmProgram VmProgram, VmState VmState) MakeProgram(Instruction[] instructions) {
        var program = ProgramCompiler.Compile(
            new LoweringResult([.. instructions], MaxActiveLocalsDepth: 1),
            mode: CompilationMode.Normal);
        var state = new VmState(program) { MaxLoopIterations = 100_000_000 };
        return (program, state);
    }
    [Test]
    public async Task Debugger_BreakpointSet_SyncsToState() {
        var instructions = new Instruction[] { new LoadConst(42), new ReturnOp() };
        var program = ProgramCompiler.Compile(new LoweringResult([.. instructions]), mode: CompilationMode.Normal);
        var state = new VmState(program) { MaxLoopIterations = 100_000_000 };
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
        var program = ProgramCompiler.Compile(new LoweringResult([.. instructions]), mode: CompilationMode.Normal);
        var state = new VmState(program) { MaxLoopIterations = 100_000_000 };
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
        var program = ProgramCompiler.Compile(new LoweringResult([.. instructions]), mode: CompilationMode.Normal);
        var state = new VmState(program) { MaxLoopIterations = 100_000_000 };
        var dbg = new VmDebugger(state, program);

        var result = dbg.Execute();
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task Debugger_InspectState() {
        var instructions = new Instruction[] {
            new LoadConst(10), new LoadConst(20), new BinOp(BinOpKind.Add), new ReturnOp()
        };
        var program = ProgramCompiler.Compile(new LoweringResult([.. instructions]), mode: CompilationMode.Normal);
        var state = new VmState(program) { MaxLoopIterations = 100_000_000 };
        var dbg = new VmDebugger(state, program);

        dbg.Execute();
        await Assert.That(dbg.StackHeight).IsEqualTo(1);
        await Assert.That(dbg.PeekStack(0)).IsEqualTo(30L);
        await Assert.That(dbg.GetInstruction(0)).IsTypeOf<LoadConst>();
        await Assert.That(dbg.GetInstruction(2)).IsTypeOf<BinOp>();
        await Assert.That(dbg.GetInstruction(3)).IsTypeOf<ReturnOp>();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Breakpoint suspend/resume — ring register save/restore + _pc dispatch
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Breakpoint_SuspendAndResume_RingRestored() {
        // µops: three pushes, breakpoint, two adds, return.
        // On breakpoint fire at PC 3, ring has three values.
        // Resume must restore _r0/_r1/_r2 so the adds produce correct result.
        var (program, state) = MakeProgram([
            new LoadConst(10),        // PC 0: _r0 = 10
            new LoadConst(20),        // PC 1: _r1 = 20
            new LoadConst(30),        // PC 2: _r2 = 30
            new BreakpointCheck(),    // PC 3: saves ring (depth=3), suspends
            new BinOp(BinOpKind.Add), // PC 4: _r1 = 20 + 30 = 50
            new BinOp(BinOpKind.Add), // PC 5: _r0 = 10 + 50 = 60
            new ReturnOp(),           // PC 6: returns 60
        ]);

        state.Breakpoints = [3];

        // First call — should suspend at PC 3
        program.Delegate(state);
        await Assert.That(state.Status).IsEqualTo(InterpreterStatus.Suspended);
        await Assert.That(state.ProgramCounter).IsEqualTo(4);
        await Assert.That(state.NeedsRingRestore).IsTrue();

        // Resume: restore ring, clear flag, continue
        state.Status = InterpreterStatus.Running;
        state.Breakpoints = null;
        program.Delegate(state);

        await Assert.That(state.Status).IsEqualTo(InterpreterStatus.Running);
        await Assert.That(state.NeedsRingRestore).IsFalse();
        long result = state.Stack.StackPointer > 0 ? state.Stack.RawSlots[0] : 0;
        await Assert.That(result).IsEqualTo(60L);
    }

    [Test]
    public async Task Breakpoint_NoBreakpoint_PassesThrough() {
        // Same µops, no breakpoint set — should complete in one pass.
        var (program, state) = MakeProgram([
            new LoadConst(10),
            new LoadConst(20),
            new LoadConst(30),
            new BreakpointCheck(),
            new BinOp(BinOpKind.Add),
            new BinOp(BinOpKind.Add),
            new ReturnOp(),
        ]);

        program.Delegate(state);

        await Assert.That(state.Status).IsEqualTo(InterpreterStatus.Running);
        await Assert.That(state.NeedsRingRestore).IsFalse();
        long result = state.Stack.StackPointer > 0 ? state.Stack.RawSlots[0] : 0;
        await Assert.That(result).IsEqualTo(60L);
    }

    [Test]
    public async Task Breakpoint_MultipleSuspendResumeCycles() {
        // Two breakpoints: verify correct state after multiple suspend/resume
        var (program, state) = MakeProgram([
            new LoadConst(7),          // PC 0: _r0 = 7
            new BreakpointCheck(),     // PC 1: breakpoint A (depth=1)
            new LoadConst(3),          // PC 2: _r1 = 3
            new BreakpointCheck(),     // PC 3: breakpoint B (depth=2)
            new BinOp(BinOpKind.Add),  // PC 4: _r0 = 7 + 3 = 10
            new ReturnOp(),            // PC 5: returns 10
        ]);

        // First breakpoint at PC 1
        state.Breakpoints = [1];
        program.Delegate(state);
        await Assert.That(state.Status).IsEqualTo(InterpreterStatus.Suspended);
        await Assert.That(state.ProgramCounter).IsEqualTo(2);
        await Assert.That(state.NeedsRingRestore).IsTrue();

        // Resume — should now hit breakpoint at PC 3
        state.Status = InterpreterStatus.Running;
        state.Breakpoints = [3];
        program.Delegate(state);
        await Assert.That(state.Status).IsEqualTo(InterpreterStatus.Suspended);
        await Assert.That(state.ProgramCounter).IsEqualTo(4);
        await Assert.That(state.NeedsRingRestore).IsTrue();

        // Resume again — should complete
        state.Status = InterpreterStatus.Running;
        state.Breakpoints = null;
        program.Delegate(state);
        await Assert.That(state.Status).IsEqualTo(InterpreterStatus.Running);
        await Assert.That(state.NeedsRingRestore).IsFalse();
        long result = state.Stack.StackPointer > 0 ? state.Stack.RawSlots[0] : 0;
        await Assert.That(result).IsEqualTo(10L);
    }

    [Test]
    public async Task Breakpoint_ResumeWithVmDebugger() {
        // BreakpointCheck instructions are sparse — placed only at Syntax
        // Node boundaries (statement boundaries), not in front of every µop.
        var (program, state) = MakeProgram([
            new BreakpointCheck(),     // PC 0: statement boundary
            new LoadConst(10),         // PC 1
            new LoadConst(20),         // PC 2
            new BreakpointCheck(),     // PC 3: statement boundary (before add)
            new BinOp(BinOpKind.Add),  // PC 4
            new BreakpointCheck(),     // PC 5: statement boundary (before return)
            new ReturnOp(),            // PC 6
        ]);

        var dbg = new VmDebugger(state, program);
        dbg.SetBreakpoint(3); // user breakpoint at PC 3
        // state.Breakpoints = [3]

        // Execute → hits BreakpointCheck at PC 3, suspends
        var result = dbg.Execute();
        await Assert.That(result.Kind).IsEqualTo(InterpreterResult.ResultKind.Suspend);
        await Assert.That(dbg.SuspendedPC).IsEqualTo(4);
        await Assert.That(state.NeedsRingRestore).IsTrue();

        // StepInto: adds step breakpoint at PC 4+1=5, calls Resume().
        // On re-entry: preamble restores ring, clears NeedsRingRestore,
        // dispatch to PC 4 (BinOp). Then PC 5 (BreakpointCheck) fires
        // because state.Breakpoints now contains [3, 5].
        // BreakpointCheck sets NeedsRingRestore = true again.
        dbg.StepInto();
        result = dbg.Execute();
        await Assert.That(result.Kind).IsEqualTo(InterpreterResult.ResultKind.Suspend);
        await Assert.That(dbg.SuspendedPC).IsEqualTo(6);
        // NeedsRingRestore was cleared on first resume, but BreakpointCheck at
        // PC 5 set it again before suspending:
        await Assert.That(state.NeedsRingRestore).IsTrue();

        // StepInto: adds step breakpoint at PC 6+1=7 (past end — won't fire).
        // On resume: restore ring, dispatch to PC 6 (ReturnOp), complete.
        dbg.StepInto();
        result = dbg.Execute();
        await Assert.That(result.Value).IsEqualTo(30L);
        await Assert.That(state.NeedsRingRestore).IsFalse();
    }

    [Test]
    public async Task Breakpoint_DirectDelegateCall_NonBreakpointEntry() {
        // Verify the preamble doesn't restore stale ring values on
        // a normal entry (no breakpoint was hit).
        var (program, state) = MakeProgram([
            new LoadConst(42),
            new ReturnOp(),
        ]);

        // Prime Registers with junk to catch accidental restore
        state.Registers = [99L, 99L, 99L];
        state.ProgramCounter = 0;
        state.Status = InterpreterStatus.Running;

        program.Delegate(state);

        long result = state.Stack.StackPointer > 0 ? state.Stack.RawSlots[0] : 0;
        // 42 came from LoadConst, not from Registers[0] restore
        await Assert.That(result).IsEqualTo(42L);
    }
}