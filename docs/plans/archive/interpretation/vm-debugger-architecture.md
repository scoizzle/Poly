> **ARCHIVED (2026-07-10)** — Do not implement. Superseded by direct AST→VM-ABI (`DirectVmAbiEmitter`). See `docs/plans/archive/interpretation/README.md`.
>
> Original document follows for historical context only.

# Plan: VmDebugger Architecture

**Date:** 2026-06-08  
**Status:** Draft  

## Problem

Today the VM (`Vm.Execute` + `VmState`) has embedded debug concepts that couple it to the AST layer:

| VM member | Dependency | Problem |
|-----------|-----------|---------|
| `VmState.BreakpointSkipNodeId` (NodeId?) | `Poly.Syntax.NodeId` | VM shouldn't know about NodeIds |
| `Bytecode.SourceMap` (Dictionary<int, NodeId>) | `Poly.Syntax.NodeId` | Only needed for debug/trace, not execution |
| `VmState.NodeDescriptions` | `Poly.Syntax.NodeId` | Same — debug-only |

A breakpoint should be **just a PC**, not "the node at a PC." The debugger's job is translating between the AST model and the PC model.

## Design

### VmDebugger (new class, `Poly/Interpretation/VmDebugger.cs`)

Owns all AST-level debug concerns. Has no dependency on `VirtualMachine/*` internals — it manipulates `VmState` through public properties (`BreakpointPCs`, `SavedPC`, `Status`).

```csharp
public sealed class VmDebugger {
    private readonly VmState _state;
    private readonly AnalysisResult? _analysis;
    private readonly Bytecode _program;

    // AST-level breakpoint storage
    private readonly Dictionary<NodeId, int[]> _nodePCs;
    private NodeId? _stepOverNodeId;

    public bool IsSuspended => _state.IsSuspended;

    public VmDebugger(VmState state, Bytecode program, AnalysisResult? analysis);
```

**Breakpoint management:**
```csharp
public void SetBreakpoint(NodeId nodeId) {
    var resolved = ResolveReplacementChain(nodeId);
    if (_nodePCs.TryGetValue(resolved, out var pcs))
        _state.BreakpointPCs.UnionWith(pcs);
}

public void RemoveBreakpoint(NodeId nodeId) {
    var resolved = ResolveReplacementChain(nodeId);
    if (_nodePCs.TryGetValue(resolved, out var pcs))
        _state.BreakpointPCs.ExceptWith(pcs);
}

private NodeId ResolveReplacementChain(NodeId id) {
    var current = id;
    while (_analysis?.GetNodeReplacement(new NodeStub(current)) is { } repl
           && repl.Id != current)
        current = repl.Id;
    return current;
}
```

**Stepping (pure PC manipulation):**
```csharp
public void StepInto() {
    _stepOverNodeId = null;
    _state.BreakpointPCs.Add(_state.SavedPC);
    _state.BreakpointSkipNodeId = null;
    Resume();
}

public void StepOver() {
    // Find the enclosing node at the current PC
    var currentNodeId = _program.GetNodeIdForInstruction(_state.PC);
    _stepOverNodeId = currentNodeId;

    // Set breakpoint on the next instruction
    _state.BreakpointPCs.Add(_state.SavedPC);
    _state.BreakpointSkipNodeId = currentNodeId;
    Resume();
}

public void Resume() => _state.Status = InterpreterStatus.Running;
```

### VM changes

**Remove from VmState:**
- `BreakpointSkipNodeId` — debugger uses `SavedPC` + its own `_stepOverNodeId`

**Keep in VmState:**
- `BreakpointPCs` (HashSet<int>) — purely PC-based, no NodeId knowledge
- `SavedPC` — already exists, used by Iret

**Keep in Bytecode:**
- `SourceMap` (Dictionary<int, NodeId>) — still needed for trace output, not execution. Remove from the execution path but keep on Bytecode as metadata.

### Remove from Vm.Execute:

The pre-dispatch breakpoint check becomes:

```csharp
// Before every instruction:
if (state.BreakpointPCs is not null && state.BreakpointPCs.Contains(instrPc)) {
    state.SavedPC = pc;
    state.Status = InterpreterStatus.Suspended;
    return InterpreterResult.Suspend();
}
```

No `BreakpointSkipNodeId` check, no `SourceMap` lookup in the breakpoint path. The debugger handles skip-logic externally by temporarily removing the breakpoint PC before resuming, then re-adding it after the instruction passes.

### Step-over without skip node

The debugger implements step-over without any VM support for "skip this node":

1. Suspend at PC = 42 (inside `SomeNode`)
2. User says "step over"
3. Debugger computes the end PC of `SomeNode` (via `NodeRanges`)
4. Debugger clears breakpoint at PC 42, adds breakpoint at that end PC
5. Resume
6. VM runs, exits `SomeNode`, hits the end-PC breakpoint, suspends
7. Debugger removes end-PC breakpoint

This shifts complexity from the VM (skip node logic) to the debugger (managing temporary breakpoints). That's the right trade — the VM stays simple, the debugger has access to all the AST context it needs.

### NodeRanges for the debugger

Bytecode grows an optional `Dictionary<NodeId, (int StartPC, int EndPC)> NodeRanges`:

```csharp
// Built during Lower() alongside SourceMap
NodeRanges[node.Id] = (startPC, code.Count);
```

This replaces the need for `BreakpointSkipNodeId` — the debugger uses ranges to compute where a node ends instead of relying on VM-level skip logic.

## Migration

| Step | What |
|------|------|
| 1 | Add `NodeRanges` to `Bytecode`, build in `Lower()` |
| 2 | Remove `BreakpointSkipNodeId` from `VmState` |
| 3 | Remove SourceMap lookup from breakpoint check in `Vm.Execute` |
| 4 | Write `VmDebugger` with breakpoint set/remove, step-into/over/out |
| 5 | Move trace/debug NodeId display off the hot path (only emit on suspend, not every instruction) |

## Files

| File | Type |
|------|------|
| `Poly/Interpretation/VmDebugger.cs` | New |
| `Poly/Interpretation/VirtualMachine/Vm.cs` | Modify (simplify breakpoint check) |
| `Poly/Interpretation/VirtualMachine/VmState.cs` | Modify (remove BreakpointSkipNodeId) |
| `Poly/Interpretation/VirtualMachine/Bytecode.cs` | Modify (add NodeRanges) |
| `Poly/Interpretation/VirtualMachine/Lowering.cs` | Modify (build NodeRanges in Lower) |
