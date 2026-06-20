using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Instructions;
using Poly.Syntax;

namespace Poly.Interpretation;

/// <summary>PC-level debugger for the compiled VM.  Works with programs
/// that have <see cref="BreakpointCheck"/> instructions (inserted by
/// lowering at Syntax Node boundaries).  The delegate runs once per
/// <see cref="Vm.Execute"/> call — breakpoints are checked inline
/// by BreakpointCheck instructions.</summary>
public sealed class VmDebugger {
    public VmProgram Program { get; }
    private readonly VmState _state;
    private readonly HashSet<int> _userBreakpoints = [];
    private readonly HashSet<int> _stepBreakpoints = [];

    public VmDebugger(VmState state, VmProgram program) {
        _state = state;
        Program = program;
    }

    public bool IsSuspended => _state.Status == InterpreterStatus.Suspended;
    public int SuspendedPC => _state.ProgramCounter;

    public bool HasBreakpoint(int pc) => _userBreakpoints.Contains(pc);

    public void SetBreakpoint(int pc) {
        _userBreakpoints.Add(pc);
        SyncBreakpoints();
    }

    public void SetBreakpoint(NodeId nodeId) {
        if (Program.SourceRanges.TryGetValue(nodeId, out var range))
            SetBreakpoint(range.FirstProgramCounter);
    }

    public void ClearBreakpoint(int pc) {
        _userBreakpoints.Remove(pc);
        SyncBreakpoints();
    }

    public void ClearAllBreakpoints() {
        _userBreakpoints.Clear();
        _stepBreakpoints.Clear();
        SyncBreakpoints();
    }

    // ── Execution control ──

    /// <summary>Step to the next µop (next BreakpointCheck instruction).</summary>
    public void StepInto() {
        if (!IsSuspended) return;
        _stepBreakpoints.Add(_state.ProgramCounter + 1);  // next µop
        SyncBreakpoints();
        Resume();
    }

    /// <summary>Step to the end of the current source node.</summary>
    public void StepOver() {
        if (!IsSuspended) return;
        int target = FindNextNodeBoundary(_state.ProgramCounter);
        _stepBreakpoints.Add(target);
        SyncBreakpoints();
        Resume();
    }

    public void Resume() {
        _state.Status = InterpreterStatus.Running;
    }

    /// <summary>Execute the program — delegate checks breakpoints
    /// inline via <see cref="BreakpointCheck"/> and suspends if hit.</summary>
    public InterpreterResult Execute() {
        _state.Status = InterpreterStatus.Running;
        Program.Delegate(_state);
        if (_state.Status == InterpreterStatus.Suspended)
            return InterpreterResult.Suspend();
        int sp = _state.Stack.StackPointer;
        if (sp <= 0) return InterpreterResult.Void;
        long raw = _state.Stack.RawSlots[sp - 1];
        _state.Status = InterpreterStatus.Completed;
        return InterpreterResult.FromValue(raw);
    }

    // ── State inspection ──

    public long ReadLocal(int localIndex) {
        return _state.Stack.RawSlots[_state.FrameBase + _state.CachedArgSlots + 1 + localIndex];
    }

    public long ReadArg(int argIndex) {
        return _state.Stack.RawSlots[_state.FrameBase + argIndex];
    }

    public long PeekStack(int depth = 0) {
        return _state.Stack.RawSlots[_state.Stack.StackPointer - 1 - depth];
    }

    public int StackHeight => _state.Stack.StackPointer;

    public Instruction? GetInstruction(int pc) {
        var instructions = Program.Instructions;
        if (pc < 0 || pc >= instructions.Count) return null;
        return instructions[pc];
    }

    public Instruction? CurrentInstruction => GetInstruction(SuspendedPC);
    public VmState State => _state;

    // ── Internals ──

    private void SyncBreakpoints() {
        var combined = new HashSet<int>(_userBreakpoints);
        combined.UnionWith(_stepBreakpoints);
        _state.Breakpoints = combined.Count > 0 ? combined.ToArray() : null;
    }

    private int FindNextNodeBoundary(int pc) {
        NodeId? currentNodeId = null;
        foreach (var (id, range) in Program.SourceRanges) {
            if (pc >= range.FirstProgramCounter && pc <= range.LastProgramCounterInclusive) {
                currentNodeId = id;
                break;
            }
        }
        if (currentNodeId is null || !Program.SourceRanges.TryGetValue(currentNodeId.Value, out var r))
            return Math.Min(pc + 1, Program.Instructions.Count - 1);
        return Math.Min(r.LastProgramCounterInclusive, Program.Instructions.Count - 1);
    }
}