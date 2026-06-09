using Poly.Interpretation.VirtualMachine;

namespace Poly.Interpretation;

internal sealed class VmDebugger(VmState state, Bytecode program) {
    private readonly VmState _state = state;
    private readonly Bytecode _program = program;
    private readonly HashSet<NodeId> _breakpointNodes = [];
    private readonly List<(int PC, byte[] Original)> _breakpoints = [];
    private Dictionary<NodeId, int[]>? _reverseSourceMap;

    public bool IsSuspended => _state.IsSuspended;
    public int CurrentPC => _state.PC;

    public IReadOnlySet<NodeId> BreakpointNodes => _breakpointNodes;

    public void SetBreakpoint(NodeId nodeId) {
        if (!_breakpointNodes.Add(nodeId))
            return;
        var pcs = GetPCsForNode(nodeId);
        foreach (var pc in pcs)
            SetBreakpointAtPC(pc);
    }

    public void RemoveBreakpoint(NodeId nodeId) {
        if (!_breakpointNodes.Remove(nodeId))
            return;
        var pcs = GetPCsForNode(nodeId);
        foreach (var pc in pcs)
            RemoveBreakpointAtPC(pc);
    }

    public void ClearAllBreakpoints() {
        // Restore all patched instructions
        foreach (var (pc, original) in _breakpoints)
            Array.Copy(original, 0, _program.Code, pc, original.Length);
        _breakpoints.Clear();
        _breakpointNodes.Clear();
    }

    public void StepInto() {
        // Set a temporary breakpoint at the next instruction (SavedPC)
        SetBreakpointAtPC(_state.SavedPC);
        _state.Status = InterpreterStatus.Running;
    }

    public void StepOver() {
        SetBreakpointAtPC(_state.SavedPC);
        _state.Status = InterpreterStatus.Running;
    }

    public void StepOut() {
        if (_state.FrameBase < 0) {
            Resume();
            return;
        }
        var hdr = FrameHeader.Read(_state.Stack.AsSpan(), _state.FrameBase);
        SetBreakpointAtPC(hdr.RetPC);
        _state.Status = InterpreterStatus.Running;
    }

    public void Resume() {
        var code = _program.Code;
        // On suspend from Int(1), the VM's SavedPC = bpPc + 5 (past the Int instruction).
        // Restore the original bytes and set PC back to the breakpoint so the instruction
        // re-executes.
        for (int i = 0; i < _breakpoints.Count; i++) {
            var (bpPc, original) = _breakpoints[i];
            if (_state.PC == bpPc + 5) {
                Array.Copy(original, 0, code, bpPc, original.Length);
                _state.PC = bpPc;
                _breakpoints.RemoveAt(i);
                break;
            }
        }
        _state.Status = InterpreterStatus.Running;
    }

    public void Dispose() {
        ClearAllBreakpoints();
        _state.Dispose();
    }

    private void SetBreakpointAtPC(int pc) {
        if (pc < 0 || pc >= _program.CodeLength) return;
        // Already has a breakpoint at this PC?
        if (_breakpoints.Any(b => b.PC == pc)) return;

        var code = _program.Code;
        // Save the original 5 bytes: the opcode at pc + the next 4 bytes
        var original = new byte[5];
        Array.Copy(code, pc, original, 0, 5);

        // Patch: Int(1) = opcode + vector 1
        code[pc] = (byte)OpCode.Int;
        code[pc + 1] = 1;
        code[pc + 2] = 0;
        code[pc + 3] = 0;
        code[pc + 4] = 0;

        _breakpoints.Add((pc, original));
    }

    private void RemoveBreakpointAtPC(int pc) {
        var idx = _breakpoints.FindIndex(b => b.PC == pc);
        if (idx < 0) return;

        var (_, original) = _breakpoints[idx];
        Array.Copy(original, 0, _program.Code, pc, original.Length);
        _breakpoints.RemoveAt(idx);
    }

    private int[] GetPCsForNode(NodeId nodeId) {
        _reverseSourceMap ??= BuildReverseSourceMap();
        return _reverseSourceMap.TryGetValue(nodeId, out var pcs) ? pcs : [];
    }

    private Dictionary<NodeId, int[]> BuildReverseSourceMap() {
        var map = new Dictionary<NodeId, List<int>>();
        foreach (var (pc, id) in _program.SourceMap) {
            if (!map.TryGetValue(id, out var list))
                map[id] = list = [];
            list.Add(pc);
        }
        return map.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
    }
}