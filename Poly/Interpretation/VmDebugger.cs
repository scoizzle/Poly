using Poly.Interpretation.VirtualMachine;

namespace Poly.Interpretation;

/// <summary>PC-level debugger for the compiled µop VM.  Manages breakpoints
/// and stepping without any AST dependency — all operations are at the µop
/// PC level.  AST-level mapping (NodeId → PCs) is handled externally via
/// <see cref="Bytecode.NodeRanges"/>.</summary>
internal sealed class VmDebugger {
    private readonly VmState _state;
    private readonly Bytecode _program;

    /// <summary>Breakpoints set by the user (not one-shot step breakpoints).</summary>
    private readonly HashSet<int> _userBreakpoints = [];

    /// <summary>One-shot breakpoints set internally by step operations.</summary>
    private readonly HashSet<int> _stepBreakpoints = [];

    public VmDebugger(VmState state, Bytecode program) {
        _state = state;
        _program = program;
    }

    public bool IsSuspended => _state.IsSuspended;
    public int SuspendedPC => _state.SavedPC;

    // ── Breakpoint management ──

    public void SetBreakpoint(int pc) {
        _userBreakpoints.Add(pc);
        SyncBreakpoints();
    }

    public void ClearBreakpoint(int pc) {
        _userBreakpoints.Remove(pc);
        SyncBreakpoints();
    }

    public void ClearAllBreakpoints() {
        _userBreakpoints.Clear();
        SyncBreakpoints();
    }

    public bool HasBreakpoint(int pc) =>
        _userBreakpoints.Contains(pc);

    // ── Stepping ──

    /// <summary>Step to the next µop (regardless of node boundary).</summary>
    public void StepInto() {
        if (!IsSuspended) return;
        // Run to the next µop
        int nextPc = _state.SavedPC + 1;
        if (nextPc < _program.CodeLength) {
            _stepBreakpoints.Add(nextPc);
            SyncBreakpoints();
        }
        Resume();
    }

    /// <summary>Step to the end of the current AST node (or the next µop
    /// if this is the last µop of the current node).</summary>
    public void StepOver() {
        if (!IsSuspended) return;
        // Find the end of the node that contains the current PC
        int currentPc = _state.SavedPC;
        int targetPc = FindNextNodeBoundary(currentPc);
        _stepBreakpoints.Add(targetPc);
        SyncBreakpoints();
        Resume();
    }

    /// <summary>Run until the current function returns.</summary>
    public void StepOut() {
        if (!IsSuspended) return;
        // Read the return PC from the frame metadata at Slot(FB + ArgSlots)
        int fb = _state.FrameBase;
        int argSlots = _state.CachedArgSlots;
        if (fb >= 0) {
            long packed = _state.Stack.RawSlots[fb + argSlots];
            int retPc = (int)(packed >> 32);
            if (retPc < _program.CodeLength) {
                _stepBreakpoints.Add(retPc);
                SyncBreakpoints();
            }
        }
        Resume();
    }

    /// <summary>Resume execution from a suspended state.</summary>
    public void Resume() {
        _state.Status = InterpreterStatus.Running;
    }

    // ── Internals ──

    /// <summary>Sync the combined breakpoint set to VmState.</summary>
    private void SyncBreakpoints() {
        if (_userBreakpoints.Count == 0 && _stepBreakpoints.Count == 0) {
            _state.BreakpointPCs = null;
            return;
        }
        var combined = new HashSet<int>(_userBreakpoints);
        combined.UnionWith(_stepBreakpoints);
        _state.BreakpointPCs = combined;
    }

    /// <summary>Find the first µop index that belongs to a different
    /// source node than the one at <paramref name="pc"/>, or the next
    /// µop if <paramref name="pc"/> is the last µop of its node.</summary>
    private int FindNextNodeBoundary(int pc) {
        if (_program.NodeRanges is null)
            return Math.Min(pc + 1, _program.CodeLength - 1);

        // Find which node contains this PC
        NodeId? currentNodeId = null;
        foreach (var (nodeId, (start, end)) in _program.NodeRanges) {
            if (pc >= start && pc < end) {
                currentNodeId = nodeId;
                break;
            }
        }
        if (currentNodeId is null)
            return Math.Min(pc + 1, _program.CodeLength - 1);

        // End of this node is the boundary
        var range = _program.NodeRanges[currentNodeId.Value];
        return Math.Min(range.EndPC, _program.CodeLength - 1);
    }
}