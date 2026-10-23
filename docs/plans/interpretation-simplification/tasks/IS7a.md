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
