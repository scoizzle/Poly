# TASK ABI5 - Register-file prune (lazy locals, grow by one, dead surface)

Status: implemented; branch `slice/abi5` (from `origin/master` 97e345b5). Review 1.
Plan: `/Users/scoizzle/.opencode/plan/abi5-register-prune.md`.

## Why
The ABI cleanup (ABI1-ABI4) left the register file alone: `_regN` is durable
per-variable storage, `_rN` is a transient expression stack, so merging them is
not a prune. The remaining waste is allocation policy:

- `Context()` eagerly created `_reg0.._reg7` and added them to the emitted block
  for every program (`AbiEmitter.Context.cs:20-40`).
- The file grew by eight when one more register was needed (`GrowRegisterFile`).
- `Context.RegisterCount` was public with zero readers.
- `WithInterrupt` was a dead private method (`AbiEmitter.Invoke.cs:891-931`).

The README's `var x = 2; x + 1` emitted 14 locals; eight were dead in `Normal`
(`_this`, `_reg1.._reg7`).

## Scope

### ABI5a - lazy register locals + grow by one + dead surface
- `Context` constructor no longer creates register locals.
- `DeclareVariable` takes the first free `_regUsed` index, else appends one
  (`AddRegister`), else falls back to the frame slot at `MaxRegisterCount = 32`.
- `PopScope` reuse, frame slots, and `VariableLayout` are unchanged.
- Delete `GrowRegisterFile`, `_registerCount`, `Context.RegisterCount`, and
  `Context(int)`.
- README: `_regN` "created on demand"; drop the unused-locals elision note.

Shape contract (`Poly.Tests/Interpretation/VmExpressionShapeTests.cs`):

| Test | Before | After |
|------|--------|-------|
| `NoVariables_EmitsNoRegisterLocals` | 8 | 0 |
| `OneVariable_EmitsOnlyItsRegister` | 8 | 1 |
| `SiblingScopes_ReuseOneRegisterLocal` | 8 | 1 |
| `NineSimultaneousVariables_GrowByOne` | 16 | 9 |
| `ThirtyThreeSimultaneousVariables_FallBackToFrameSlots` | 32 + slot | 32 + slot (characterization) |

Runtime companion in `DirectVmAbiEmitterTests`:
`LargeLocalSet_RegisterAndFrameSlotVariablesCoexist` executes 40 locals and
reads `v0`, `v31` (last register), `v32` (first slot), and `v39` with distinct
weights, so an off-by-one at the boundary changes the result.

### ABI5b - delete dead `WithInterrupt`
`AbiEmitter.Invoke.cs` private method with no callers; `CompileStatement` is the
live equivalent. No behavior change; the suite is the oracle.

## Files
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Context.cs`
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Invoke.cs`
- `Poly/Interpretation/Vm/README.md`
- `Poly.Tests/Interpretation/VmExpressionShapeTests.cs`

## Done when
- The four red shape tests pass; the fallback characterization still passes.
- `RegisterCount`, `GrowRegisterFile`, and `_registerCount` are extinct; `Context`
  is constructed only as `new Context()`.
- A declared variable still gets its frame slot and `VariableLayout`; the 33rd
  variable reads/writes `_slots[_fp + 32]`.
- `WithInterrupt` is extinct.
- Build clean; full suite green with the pass count recorded.
- `scripts/restart-poly-mcp.sh` run after the emitter change.

## Public surface
`AbiEmitter.Context(int)` and `AbiEmitter.Context.RegisterCount` are deleted. Both
are public; no in-repo reader remains, but an external consumer cannot be proven
absent by grep (the same risk ABI2 accepted).

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-21 | OpenCode | local | pending | implemented; red 8/8/8/16 recorded before the allocator edit with `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmExpressionShapeTests/*'` | none |
| 2026-10-21 | OpenCode | local | pending | green: shape 8/8, build clean, full suite 4190/4190 (adds the 40-local runtime boundary test); `scripts/restart-poly-mcp.sh` run. Change verifier COMPLETE (no 🔴/🟠 + 3 nits fixed); second opinion COMPLETE (no 🔴/🟠, 2 log nits addressed here) | none |
