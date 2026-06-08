using Poly.Interpretation.VirtualMachine;

namespace Poly.Interpretation;

internal sealed class VmDebugger(VmState state, Bytecode program) {
    private readonly VmState _state = state;
    private readonly Bytecode _program = program;
    private readonly HashSet<NodeId> _breakpointNodes = [];
    private Dictionary<NodeId, int[]>? _reverseSourceMap;

    public bool IsSuspended => _state.IsSuspended;
    public int CurrentPC => _state.PC;

    public IReadOnlySet<NodeId> BreakpointNodes => _breakpointNodes;

    public void SetBreakpoint(NodeId nodeId) {
        if (!_breakpointNodes.Add(nodeId))
            return;
        var pcs = GetPCsForNode(nodeId);
        (_state.BreakpointPCs ??= []).UnionWith(pcs);
    }

    public void RemoveBreakpoint(NodeId nodeId) {
        if (!_breakpointNodes.Remove(nodeId))
            return;
        var pcs = GetPCsForNode(nodeId);
        _state.BreakpointPCs?.ExceptWith(pcs);
    }

    public void ClearAllBreakpoints() {
        _state.BreakpointPCs?.Clear();
        _breakpointNodes.Clear();
    }

    public void StepInto() {
        (_state.BreakpointPCs ??= []).Add(_state.SavedPC);
        _state.Status = InterpreterStatus.Running;
    }

    public void StepOver() {
        (_state.BreakpointPCs ??= []).Add(_state.SavedPC);
        _state.Status = InterpreterStatus.Running;
    }

    public void StepOut() {
        if (_state.FrameBase < 0) {
            Resume();
            return;
        }
        var hdr = FrameHeader.Read(_state.Stack.AsSpan(), _state.FrameBase);
        (_state.BreakpointPCs ??= []).Add(hdr.RetPC);
        _state.Status = InterpreterStatus.Running;
    }

    public void Resume() {
        _state.Status = InterpreterStatus.Running;
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