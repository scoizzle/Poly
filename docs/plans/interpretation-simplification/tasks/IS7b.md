# TASK IS7b - typed storage for variables (register and frame)

Status: **merged** `11e5b647` (#291), together with IS7a. Branch `slice/IS7b` was stacked on `slice/IS7a`. Lane C (`vm`). Depends on IS7a.
Plan: [IS-README](../IS-README.md). Analysis: [`floating-point-representation.md`](../../../analysis/floating-point-representation.md).
Lands with [IS7a](IS7a.md) - the pair is measured as one change.

## Why

IS7a typed the ring and the FP benchmark did not move. The measurement says why: mandelbrot's hot state is **variables** (`zr`, `zi`, `cr`, `ci`), and a variable is backed by:

- a **register**: `Variable(typeof(long), "_reg{n}")` (`AbiEmitter.Context.cs:348`), the first 32 variables; or
- a **frame slot**: `_slots[_fp + n]` in a `long[]` (`Context.SlotsLocal`, `SlotLocation.Frame`).

Both are words, so every read of a `double` variable is `Int64BitsToDouble` and every write is `DoubleToInt64Bits` - per *access*, several times per loop iteration, which is the entire remaining cost.

Hand-written A/B at this benchmark's exact shape: all-bits `long` locals **124 us**, typed `double` locals **64 us**, current VM **131 us**. So ~1.9x is available here, and only here.

## Scope

Phase 1 - **registers** (`_regN`). That is where a top-level program's variables live (`MaxRegisterCount = 32`), and it is where the benchmark's state sits.

- `AddRegister()` gains the variable's representation: `_regN` is `double` for an IEEE variable, `long` otherwise.
- `SlotLocation.Local` and every reader of `_regVars` carry the type rather than assuming `long`.
- **The collision to handle explicitly:** a register index is bound to a `Variable` by `_variableRegisters`, but liveness lets an index be reused. If two variables of different representations can share an index across scopes, they must not alias one `ParameterExpression`. Handle it the way IS7a handled the ring - key by (index, representation) - or prove reuse cannot cross representations and say so in the Log with the evidence.
- A boundary helper: a typed register written to a word (`_slots`, capture cell, call result) converts once; a word read into a typed register converts once. One conversion per crossing, not per access.

Phase 2 - **frame slots**, only if phase 1 leaves a measured gap. `_slots` is a monomorphic `long[]`, so typing it means per-representation arrays or a byte-addressed arena; that is a larger change and should not be bundled with phase 1. If the benchmark is fully register-backed after phase 1, stop and record that.

## Files

- `Poly/Interpretation/Vm/Abi/AbiEmitter.Context.cs` - `AddRegister`, `_regVars`, `Resolve`.
- `Poly/Interpretation/Vm/Abi/SlotLocation.cs` - `Local` and `Frame` carry representation.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Statements.cs` / `.Expressions.cs` - readers of register values.
- `Poly.Tests/Interpretation/*` and `Poly.Tests/TestHelpers/VmTree.cs`.
- `docs/interpretation/vm-abi-reference.md`.

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| `zr * zr` where `zr` is a double variable | read `_reg` + bitcast, multiply, bitcast | read `double _reg`, multiply, no bitcast | `DoubleVariableArithmetic_HasNoBitcast` (structural, `VmTree.Scan`) |
| a double variable assigned then read in the next statement | bitcast on both | no bitcast (same register) | `DoubleVariable_AcrossStatements_KeepsItsRegister` |
| a double variable passed to a CLR call | bitcast at the call | one bitcast at the call | `DoubleVariable_ToClrCall_ConvertsOnce` |
| a double variable captured in a closure | bitcast through the cell | one per cell access | `DoubleVariable_Captured_ConvertsAtTheCell` |
| two variables of different representations reusing one index | one `long` local | distinct typed locals, or reuse proven safe | `RegisterIndex_ReusedAcrossRepresentations_DoesNotAlias` |
| an integer variable | `long` register | unchanged in phase 1 | `IntegerVariable_StaysAWord` |

## Done when

- A structural test proves no bitcast remains between a `double` variable and its arithmetic.
- The alias row passes: either distinct locals, or documented proof reuse cannot cross representations.
- **Benchmark gate, `--job medium`, IS7a+IS7b measured together against the plan branch:**
  - `ProgramBenchmarks` Mandelbrot: expect a large improvement (the A/B says ~2x is available).
  - **Sieve, Collatz, NQueens must not regress** past noise.
  - Also report the double-precision mandelbrot from `docs/perf-comparison` if it can be run.
- `AstParityTests` 145/145; full suite green; build 0 warnings.
- `docs/interpretation/vm-abi-reference.md` states the storage rule for both ring and variable.

## SHIP if / NOT SHIP if

SHIP: the FP benchmark improves materially, the other three hold, and the alias row is proven.
NOT SHIP: registers are typed but the FP benchmark is flat (means the cost is elsewhere - report where, do not merge); an integer program regresses; the alias question is answered by luck rather than a test.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*DoubleVariable*/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmTreeInvariantTests/*'
dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job medium
```

## Hand-edit

A variable's register is declared with the variable's own type, in one method; the conversions are at the four places a value leaves a register.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-23 | agent | Copilot/VS Code | `e84cbcbd` | **SHIP.** Registers typed; **Mandelbrot 131.7 → 68.2 us (1.93×)**, the other three hold. Phase 1 only — phase 2 (frame slots) not needed and not attempted | **The rule.** `Context.RegisterVar(index, representation)` declares `_dregN` `double`, `_fregN` `float`, `_regN` word, lazily; `_variableRegisters` maps a variable to `(index, representation)`. **The collision, answered structurally, not by luck:** a register index is released by `PopScope` and reused by the next scope's variable, so index 0 can back a `double` in one block and a word in the next; the table is keyed by (index, representation) exactly as IS7a keyed the ring, so the two never alias one `ParameterExpression` and `Expression.Assign` stays fail-closed. `RegisterIndex_ReusedAcrossRepresentations_DoesNotAlias` (two sibling blocks, one `double`, one `long`, both index 0) proves it: `_dreg0:Double` and `_reg0:Int64` are distinct locals and both scopes read what they wrote. **A cell-backed local keeps the word**, because its register holds the capture cell's heap handle, not the value — typing it by the value would coerce the handle (caught by `DoubleVariable_Captured_ConvertsAtTheCell`). `SlotLocation` now states its `Representation`, `ReadAs`/`Write` convert once when it differs, `Read`/`VariableRead` stay the word contract. **One real bug the suite caught:** the destination's *value* representation and its *storage* representation are different questions — a cell-backed `double` has a word storage — so `AssignedValue` decodes the emitted value by the analysis type and then numerically converts to the variable's type; my first cut converted to the storage representation and turned `double x = 1.5` into `1` (`Run_TypedLocal_DoubleDividesAsDouble`, `ShadowedName_OuterUseAfterInnerScope_KeepsOuterType`, `ShadowedLoopVariable_DoesNotTakeTheOuterType`). **RED, before the change** (`--treenode-filter '/*/*/*TypedVariableStorageTests/*'`): `total: 6 failed: 4 succeeded: 2` — `_reg0:Int64, _reg1:Int64` for two double variables; `Int64BitsToDouble(_reg0)` at a call boundary; no `_dreg0` at all. The 2 that passed did so for the right reason (a captured local's register is a word, an integer variable is a word) and are the shape matrix's negative rows. **GREEN:** `TypedVariableStorageTests` 6/6, `IeeeRingSlotTests` 6/6 (one expectation retargeted — see below), `AstParityTests` **145/145**, full suite **4505/4505**, `dotnet build Poly.Benchmarks` 0 warnings. **Benchmark `--job medium`, `dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job medium`, baseline on this branch's HEAD (IS7a) → after:** **Mandelbrot 131.7 → 68.2 us (−48.2 %, 1.93×)** — the hand-written typed-double A/B was 64 us, so the VM is now within 6.6 % of it and the 1.94× the analysis predicted is collected; **Sieve 101.0 → 99.0 (−2.0 %)**, **Collatz 139.7 → 135.3 (−3.1 %)**, **NQueens 168.6 → 168.6 (flat)** — a NQueens-only re-run reproduces the baseline mean exactly; a 169.9 reading in the batch run is cross-benchmark noise (StdDev 2.0 there vs 0.6 alone). **Phase 2 not needed — evidence.** Top-level mandelbrot body emits **12 registers**: `_reg0.._reg4` (side, maxIter, total, y, x), `_dreg5.._dreg8` (cr, ci, zr, zi), `_reg9` (iter), `_dreg10`, `_dreg11` (zr2, zi2) — 7 typed double, 5 word, **0 register bitcasts**, `MaxRingDepth` unchanged at 13. The whole body keeps **22** bitcasts, none of them a frame-slot *variable read*: **4** are CLR conversion-call results (`(double)x`, `(double)side`, `(double)y`, `(double)side`), **6** are block-exit `_slots` snapshots for the IEEE locals (cr, ci, zr, zi, zr2, zi2) that `VmDebugger.GetLocals` needs, and **12** are `RingRegion` word folds — an assignment's own result landing on the word ring (`_r4` ×4, `_r5` ×6, `_r7` ×2). So the residual is the ring's word-addressed region contract and the debug snapshot, not frame slots; typing `_slots` would buy nothing for this program. **The IS7a test retargeted, not weakened:** `IeeeAcrossStatements_CrossesTheBoundaryOnce` asserted 3 bitcasts for "two double stores and one double read"; with typed registers those two stores and that read cost **0**, and the 5 that remain are all boundaries (2 region folds, 2 frame snapshots, 1 root copy-out). It now asserts `RegisterBitcasts` is empty *and* names each of the five. **Shape, measured:** IEEE arithmetic converting at the operation stays 2/14 (the two `(double)` casts), register-boundary bitcasts 0. Do not cite this slice as an integer-width change — an integer variable is still one word. |

