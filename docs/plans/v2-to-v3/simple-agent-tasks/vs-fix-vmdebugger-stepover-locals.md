# Micro-Task: Fix flaky VmDebugger_StepOver_TraversesStatements (locals)

**Suite:** [`vs-README.md`](vs-README.md) **#polish-dbg**  
**Priority:** High for CI green — may run **in parallel** with Slice 3 (no MCP file thrash)  
**Difficulty:** Small–Medium  
**Estimated Context:** ~5k tokens  
**Status:** [x] Done — ValueStack.ClearRented + CaptureResult uses hook snapshot  

## Objective

Make `VmDebugger_StepOver_TraversesStatements` **stable under full suite** (not only in isolation). Today it fails when `Start()` asserts `x`/`y` are 0 because locals are read from **dirty ArrayPool stack slots**.

## Root cause (research 2026-07-12)

1. `ValueStack` rents from `ArrayPool<long>.Shared` **without clearing** → leftover garbage (e.g. `4545014856`, `42`).
2. Hook builds a correct snapshot via `GetLocals(program, span)` into `CurrentLocals`.
3. `CaptureResult()` **ignores** that and re-reads `GetLocals(_state)` from `RawSlots` at fixed `fp=0`.
4. At root `Block` pause, vars may not be flushed yet → assert sees pool dirt.

Repro: full `dotnet run --project Poly.Tests` → fail at line asserting `Start` locals == 0. Isolated filter often passes.

## Required Reading

- `Poly/Interpretation/Vm/VmDebugger.cs` — `DebugHookHandler`, `CaptureResult`, `GetLocals`
- `Poly/Interpretation/Vm/ValueStack.cs` — `ArrayPool` rent
- `Poly.Tests/Interpretation/DirectVmAbiEmitterTests.cs` — `VmDebugger_StepOver_TraversesStatements`

## Exact Steps

1. **Prefer:** `CaptureResult` / `Start` return path use **hook snapshot** (`CurrentLocals` set in hook) when pause was from a step, not a fresh `GetLocals(_state)` re-read of dirty slots.
2. **And/or:** Clear rented buffer on `ValueStack` construction (or after `Rent`) so uninitialized slots are 0.
3. Keep multi-step StepOver behavior (`_stepRequested` not cleared in hook).
4. Run full test project once and confirm `VmDebugger_StepOver_TraversesStatements` is green under suite load.
5. Optional: assert on `debugger.CurrentLocals` in the test as well.

## Verification

- [ ] Full suite green including this test
- [ ] Test still passes alone
- [ ] No change to non-debug execution path performance (clearing stack is fine)

## Output

- `VmDebugger.cs` and/or `ValueStack.cs` (+ maybe test)
- Summary under `../agent-summaries/`

## Out of Scope

- Full debugger redesign
- MCP tools

## Status tracking

**Claimed by:**  
**Started:**  
**Notes:**
