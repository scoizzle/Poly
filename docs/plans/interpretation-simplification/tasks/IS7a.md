# TASK IS7a - typed ring slots for IEEE values

Status: planned. Branch `slice/IS7a`. Lane C (`vm`). Depends on IS5 (merged).
Plan: [IS-README](../IS-README.md). Analysis: [`floating-point-representation.md`](../../../analysis/floating-point-representation.md).

## Why

The ring declares every slot as `Variable(typeof(long), "_r{n}")` (`AbiEmitter.Context.cs:220-229`), so a `double` can only live as its bit pattern. `EmitBinaryArithmeticValue` (`AbiEmitter.cs:570-582`) therefore emits, per operation: ring load, `Int64BitsToDouble` per operand, the arithmetic, `DoubleToInt64Bits`, ring store. `zx*zx + zy*zy` is six bitcasts, and the value cannot stay in a double register across an operation boundary.

Measured ceiling (`floating-point-representation.md` §The measurement that frames everything): hand-written C# in exactly the emitter's shape costs 2,350 us, matching the Poly row at 2,226 us, while nested double expressions cost 1,320 us. **1.8x, and ~0 of it is interpreter dispatch.**

This slice types the ring for IEEE values only. Integer widths are IS7b, because they change overflow semantics and depend on [#289](https://github.com/scoizzle/Poly/issues/289).

## Scope

- `Context` gains a slot table keyed by **(index, representation)** rather than index alone. `RingVar(int)` keeps returning the `long` local; `RingVar(int, Type)` returns the local for that representation, created lazily.
  - **This is load-bearing:** ring depth rewinds after every statement, so index 3 legitimately holds a `double` in one statement and a `long` in the next. One local per index cannot serve both - `Expression.Assign` to a `ParameterExpression` of the wrong type throws. Distinct locals per (index, representation) keeps every assignment type-correct.
- An IEEE-producing arm stores into a `double` slot and reads it as `double`: no `ToIeee`/`FromIeee` inside an expression.
- The bitcasts survive **only where a word must move as a word**: `_slots[_fp + n]` frame read/write, `VmCall.Invoke`'s result, capture cells, and boxing into a heap handle. Those are per-statement or per-call, not per-operation.
- `double` and `float` both. `float` stays `float` within an expression; widening to `double` happens only at a word boundary.

## Files

- `Poly/Interpretation/Vm/Abi/AbiEmitter.Context.cs` - the slot table and `RingVar`.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.cs` - `EmitBinaryArithmeticValue`, `AsIeeeDouble`, `ValueOf`, `CompileNodeInner`'s IEEE arms, comparisons.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Expressions.cs` - `EmitUnaryMinus`, `EmitConditional`, `EmitCoalesce` if they carry IEEE.
- `Poly/Interpretation/Vm/Abi/RingCodec.cs` - only if a helper's contract changes.
- `Poly.Tests/TestHelpers/VmTree.cs` - the slot-type invariant below.
- `Poly.Tests/Interpretation/*` - the tests.
- `docs/interpretation/vm-abi-reference.md` - the ring's representation rule.

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| `zx*zx + zy*zy` on doubles | 6 bitcasts, all through `long` slots | 0 bitcasts inside the expression | `IeeeArithmetic_HasNoBitcastBetweenOperands` (structural, via `VmTree.Scan`) |
| a double stored then read by the next statement | `ToIeee` store, `FromIeee` read | same (word boundary) | `IeeeAcrossStatements_CrossesTheBoundaryOnce` |
| `double` compared to `double` | `FromIeee` on both sides | direct double compare | `IeeeComparison_ComparesDoubles` |
| a `float` expression | widened to `double` per operand | stays `float` | `FloatExpression_StaysNarrow` |
| a slot reused for `double` then `long` | one `long` local | two locals, both type-correct | `RingSlot_HoldingTwoRepresentations_DeclaresTwoLocals` |
| unknown representation | `long` word | unchanged - the word form remains the fallback | `UnknownRepresentation_UsesTheWordSlot` |

## Done when

- No `Int64BitsToDouble`/`DoubleToInt64Bits` call appears inside an expression that is entirely IEEE. The structural test scans the emitted tree and fails if one does.
- A slot-type invariant lands in `VmTree`: every `Assign` target's declared type matches its value's type (the compiler enforces this, so the test's job is to catch a slot table that hands out the wrong local - assert slot identity per representation).
- `AstParityTests` 145/145 - the double rows are already in its corpus.
- Full suite green.
- **Benchmark gate, `--job medium`, recorded before and after:**
  - `mandelbrot_polyvm_dbl` / the double variant: expect the measured 1.8x.
  - **Sieve must not erode** - it is where IS3's 38% came from, and a slot table that grows local count could cost there. If Sieve regresses beyond noise, stop and report.

## SHIP if / NOT SHIP if

SHIP: the structural bitcast test passes, the FP benchmark improves, Sieve holds, and the slot table is keyed by representation.
NOT SHIP: a single local serves two representations (that will throw, or worse, coerce); the fix special-cases `double` in the arithmetic arms instead of the slot; Sieve regresses and the number is not reported.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Ieee*/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmTreeInvariantTests/*'
dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job medium
```

## Hand-edit

The slot table is one dictionary and one method; a reader can see that a double lives in a double local and find the four places a word boundary converts.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-23 | agent | Copilot/VS Code | `6bbf9376` | SHIP the representation rule; **the FP benchmark does not move** — reported, not papered over | **The slot table.** `Context.RingVar(int, Type)` keeps one local per (index, representation): `_rN` word, `_dN` `double`, `_fN` `float`, created lazily. `RingVar(int)` still returns the word. `SlotLocation.Ring` carries the representation, so `Read`/`Write` land on the right local and an assign to the wrong one fails closed instead of coercing. `Compile` reports the result at the arm's **own expression type** (`code.Type`), not from the analysis, so a producer and its consumer can never disagree about which local was written. `RingRegion` slots stay words (`ToWord` on the fold), which is what let every existing region consumer — invoke arguments, array element addresses, `StmtDebugHook` spans — keep reading `RingVar(slot)` unchanged. The word remains the fallback: `SlotTypeOf` answers `long` for a representation the analysis cannot resolve. **RED, before the change** (`--treenode-filter '/*/*/IeeeRingSlotTests/IeeeArithmetic_HasNoBitcastBetweenOperands'`, emitter reverted, API kept): `total: 1 failed: 1` — `collection contains items: [Add(Double, Double), Multiply(Double, Double), Multiply(Double, Double)]`, i.e. all three IEEE operations converted their operands. **GREEN:** `IeeeRingSlotTests` 6/6, `AstParityTests` **145/145**, `VmTreeInvariantTests` 7/7, full suite **4499/4499**, `dotnet build Poly.Benchmarks` 0 warnings. **Three real bugs the existing suite caught, all consequences of the new representation and fixed at their boundary via `RingWord`:** (1) `EmitBlock`'s block-exit snapshot store trailed the value, so `Block.Type` was `long` and a block yielding a double was read as bits (`Run_TypedLocal_DoubleDividesAsDouble` 0.5 → 0, `BlockTail_AlreadyTypedDeclaration_IsNotOverwritten` 2.0 → 1E-323) — a block now re-reads its live value slot when the value is typed, and appends nothing when it is a word, so integer programs are byte-identical; (2) `CompileFunctionBody`'s result flush read the word local (`DelegateReturningDouble_BitcastsTheIeeeSlot`, `Stored_DoubleCapture_*` → 0); (3) the inline-lambda copy-out did the same. **Shape, measured on a top-level mandelbrot (throwaway probe on both trees, deleted):** ring locals 11 word / 0 double → 6 word / 9 double (15 total); total bitcasts **48 → 28**; IEEE operations converting at the operation **14/14 → 2/14**; `MaxRingDepth` unchanged (11), so the ring grew no slots. The two remaining are `(double)x` / `(double)y` — CLR conversion-call results, the word boundary the card sanctions. **Benchmark `--job medium`, `dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job medium`, before → after:** NQueens 176.15 → 170.46, **Sieve 98.98 → 98.67 (held, −0.3 %)**, **Mandelbrot 130.24 → 131.39 and 130.90** (two independent after-runs; the spread is inside the before-run's own ±1.75 StdDev), Collatz 137.60 → 134.39. **Why the expected 1.8x is absent — this is the finding.** A hand-written A/B at the benchmark's exact shape (40×40×50, checksum 32828): all-bits `long` locals **124 us**, typed `double` locals **64 us** (1.94x), Poly VM **131 us** — i.e. the VM is already at the hand-written *bits* cost and the 1.94x is real. So the cost is not the bitcast opcode (it is a free `movq`): it is forcing a `double` through a GPR-resident `long` local at every statement boundary. Removing the per-*operand* bitcasts (14/14 → 2/14) saves nothing; removing the per-*statement* storage bitcast halves the time. The residual is **variable storage** — `_regN` registers and `_slots[_fp + n]` frames — which `floating-point-representation.md`'s conversion table keeps as words. Per this card's stop condition that path is **reported, not special-cased**: the ring is now typed, and typed local storage (register first: same (index, representation) trick, no frame-array redesign) is the slice that carries the measured 1.94x. Do not cite this slice as an FP speedup. |
