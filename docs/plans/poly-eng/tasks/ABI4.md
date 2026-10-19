# TASK ABI4 - SetArgs ring-spill consolidation (no frame-protocol change)

Status: planned; branch `slice/abi4` (stacked on ABI3). Review 1.
Plan: the external ABI cleanup plan (`/Users/scoizzle/.opencode/plan/abi-cleanup.md`) (ABI4 slice).

## Scope
Replace the SetArgs inline path's `state.Registers[sp + k]` ring spill with
per-invocation CLR locals, then delete `VmState.Registers` and every allocation,
prologue, and test initializer for it. No frame-protocol change: routing this
path through `CompileFunctionBody`/`VmCall` is out (caller-context captures,
suspend-through-finally, SP-0 seeding).

## Characterization contract (green before the edit)
- seeded `SetArgs` slots are not clobbered (SP-0 seeding)
- the result stays a ring slot for the root epilogue
- captured variables read and written inside a SetArgs body
- a SetArgs body that invokes a stored closure
- suspend/resume through a SetArgs body
- the ABI1 parameter/local reservation
- the `Compile(..., rootParameters)` root door is unchanged

## Files
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.Invoke.cs`, `DirectVmAbiEmitter.cs`, `VmState.cs`
- `Poly/Interpretation/Interpreter.cs`, `Vm/VmDebugger.cs`
- `Poly.Tests/Interpretation/VmDebuggerTests.cs`, `InterpretationStabilizationTests.cs`

## Done when
- `VmState.Registers` extinct (member, allocations, prologues, initializers).
- The SetArgs path emits no array-based ring spill.
- Characterization contract green; full suite green.

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-19 | OpenCode | local | pending | planned | plan review closed before implementation |
