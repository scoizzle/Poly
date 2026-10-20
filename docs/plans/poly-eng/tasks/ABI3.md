# TASK ABI3 - Ring discipline completion + one heap-shape accessor

Status: implemented on `slice/abi3` (stacked on ABI2). Review 1 per concern.
Plan: the external ABI cleanup plan (`/Users/scoizzle/.opencode/plan/abi-cleanup.md`) (ABI3 slice).

## Scope
Part A: retire the caller-side parallel slot arrays and the remaining unconditional
`RingDepth - 1` operand captures with `AbiCtx.RingRegion`. Part B: one heap-shape
accessor on `RingCodec` for VM arrays, cells, and closure cells, and a single
`UnsafeGet` deref idiom in the emitter.

Stay list (not migrated): `EmitLogicalAnd`/`Or` right operands (short-circuit),
`SpillRingRead`, the root result write, `RingRegion.Add` itself, the SetArgs and
`CompileFunctionBody` result slots, switch arms, throw, using.

## Files
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.{cs,Expressions,Statements,Invoke,AbiCtx}.cs`
- `Poly/Interpretation/Vm/RingCodec.cs`
- Tests only if a characterization is needed (none expected; suite is the oracle)

## Done when
- Part A: no caller-side `argSlots`/`astArgSlots`/operand-triplet arrays remain;
  `RingDepth - 1` confined to `AbiCtx`/`RingRegion` and the stay list.
- Part B: element-access `long[]`/`object[]` casts and `HeapRawSlots` live only in
  `RingCodec`; the `TypeIs(long[])` unknown-element fallback exists once; the
  closure `+1` header offset is one shared constant.
- Build clean; full suite green.

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-19 | OpenCode | local | pending | Part A implemented; suite green | none |
