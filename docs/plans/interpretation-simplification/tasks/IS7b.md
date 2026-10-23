# TASK IS7b - typed storage for variables (register and frame)

Status: planned. Branch `slice/IS7b`, stacked on `slice/IS7a`. Lane C (`vm`). Depends on IS7a.
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
