# TASK ABI2 - Dead ABI state + one scalar predicate

Status: implemented on `slice/abi2` (stacked on ABI1 / PR #239). Review 1.
Plan: [`abi-cleanup.md`](/Users/scoizzle/.opencode/plan/abi-cleanup.md) (ABI2 slice).

## Scope
Delete the dead `VmProgram`/`VmState` ABI state, delete the duplicate scalar
predicate, move the combined `InvokeNamed` predicate into `RingCodec` with its
exact truth table, and derive `KindFor(object?)` from `TryInlineConstant`.

## Files
- `Poly/Interpretation/Vm/VmProgram.cs`, `VmState.cs`, `FunctionEntry.cs` (deleted)
- `Poly/Interpretation/Vm/RingCodec.cs`
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs`, `DirectVmAbiEmitter.Invoke.cs`
- `Poly/Interpretation/Vm/README.md`
- `Poly.Tests/Interpretation/DirectVmAbiEmitterTests.cs`, `RingCodecTests.cs`

## Done when
- No `*.cs` reference to `VmProgram.Functions`, `.CallSites`, `.RegisterCount`,
  `FunctionEntry`, `OldFramePos`, or `ArgumentIsStackScalar`.
- The `InvokeNamed` scalar predicate exists once (`RingCodec.IsInvokeArgumentScalar`)
  and its truth table is pinned by `IsInvokeArgumentScalar_StringConcatAdd_StaysScalarBoxed`.
- `KindFor(object?)` is derived from `TryInlineConstant`.
- Build clean; full suite green.

## Hand-edit
Each removal is a name deletion or a one-line redirection; the predicate has one home.

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-19 | OpenCode | local | pending | ABI2 implemented; build clean, suite 4152/4152 | plan review closed before implementation |
