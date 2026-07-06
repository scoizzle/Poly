# ADR: Breakpoint Architecture — PC-Level Interrupts

**Date:** 2026-06-08  
**Status:** Accepted — **Partial** (DebugInterrupt callback functional; BreakpointPCs and Int vector 1 not implemented)  

## Context

Debugging macro execution requires the ability to pause at an instruction, inspect VM state (stack, heap, locals), and resume. The previous design assumed breakpoints would be inserted as AST nodes (`BreakpointNode`) via lowering — the modeler wraps a section in a breakpoint, re-lowers, and runs. This approach was rejected for three reasons:

1. **AST modification invalidates analysis caches.** Every re-lower requires re-analysis, which is expensive.
2. **Cannot target compiled function bodies.** Lambda bodies are pre-lowered before the root expression. Changing the AST after the fact doesn't retroactively update emitted bytecode.
3. **Couples debug machinery to program representation.** Models and tools should not need to understand AST structure to set breakpoints.

## Decision

Breakpoints operate at the **bytecode PC level** via the existing `Int`/`Iret` interrupt mechanism.

### Contract

1. **`VmState` gains a `HashSet<int> BreakpointPCs`** (or sorted array for binary search). Managed externally by the debugger.

2. **`OpCode.Int` gains vector `1` as breakpoint-hit.** Vector `0` (suspend) remains unchanged.

3. **Before each instruction dispatch**, the execute loop checks `BreakpointPCs.Contains(pc) && pc != state.BreakpointSkipNodeId`. On hit, it suspends and returns `InterpreterResult.Suspend` with the current `pc`, `NodeId` (via `SourceMap`), `Stack`, and `Heap` accessible on `state`.

4. **Single-step** is implemented as: record current PC, set `BreakpointSkipNodeId = current`, add `PC + nextInstructionLength` to `BreakpointPCs`, then `Iret` to resume.

5. **The `SourceMap` (PC → NodeId)** provides the reverse mapping for debugger UI. No AST changes needed.

### Implementation sketch

```csharp
// VmState additions
public HashSet<int>? BreakpointPCs { get; set; }

// In Execute loop, before switch:
if (state.BreakpointPCs?.Contains(pc) == true
    && pc != state.BreakpointSkipNodeId) {
    state.SavedPC = pc;
    state.Status = InterpreterStatus.Suspended;
    return InterpreterResult.Suspend();
}

// OpCode.Int vector 1:
case OpCode.Int:
    int vector = ReadInt32(code, ref pc);
    state.SavedPC = pc;
    if (vector == 1 && state.BreakpointPCs?.Contains(instrPc) == true) {
        state.Status = InterpreterStatus.Suspended;
    } else if (vector == 0) {
        state.Status = InterpreterStatus.Suspended;
    }
    break;
```

## Rationale

- Zero AST changes. Zero re-analysis. Zero re-lowering.
- Works on any instruction in any function body, including pre-compiled lambdas.
- Reuses the existing suspend/resume contract (`Int`/`Iret`).
- The `BreakpointSkipNodeId` field (already declared on `VmState`) provides single-step without infinite re-suspend.
- Keeps the VM decoupled from any debug representation in the IR.

## Consequences

- `Int` vector `1` is reserved for breakpoints. Vector `0` remains general suspend.
- `VmState` gains the `BreakpointPCs` set.
- The debugger (future tooling layer) manages breakpoints externally and reads VM state on suspend.
- No new opcode needed.
