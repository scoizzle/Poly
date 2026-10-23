# Floating-point representation: where the 1.8× goes

**Status:** research + proposal. No production change accompanies this note. Measurements taken 2026-10-23 on `plan/interpretation-simplification`.
**Scope:** `Poly/Interpretation/Vm/Abi/` — the ring slot type, `RingCodec.ToIeee`/`FromIeee`, and the arithmetic arms.
**Input:** the double-precision mandelbrot rows in [`docs/perf-comparison/results/results_20261023_070816.csv`](../perf-comparison/results/results_20261023_070816.csv).

## The measurement that frames everything

The VM's dispatch is not the problem. A hand-written C# function, laid out in **exactly the shape the emitter produces** (every intermediate a `long` local holding IEEE bits, `BitConverter` around every operation), costs the same as the Poly program:

| Shape | Time (128×128, 256 iter) | Source |
|---|---|---|
| Ring model, hand-written C# | ~2,350 us | this note's A/B |
| **Poly VM double (bits)** | **2,226 us** | CSV |
| Nested double expressions, hand-written C# | **~1,320 us** | this note's A/B |
| Poly VM fixed-point integer | 1,069 us | CSV |

Ratio ring-model / nested-double: **1.76-1.80x** across three rounds, identical checksum.

So roughly **1.8x of FP cost is the emitted representation, and ~0 is interpreter dispatch.** That is unusually good news: the fix is in one place.

## Why the shape costs that much

`Context.RingVar(int)` declares every ring slot as `Variable(typeof(long), "_r{n}")` (`AbiEmitter.Context.cs:220-229`). A `double` therefore has nowhere to live except as its bit pattern, so:

`EmitBinaryArithmeticValue` (`AbiEmitter.cs:570-582`) emits, per operation: ring load, `Int64BitsToDouble` per operand, the arithmetic, `DoubleToInt64Bits`, ring store. `zx*zx + zy*zy` is six bitcasts. Even with `BitConverter` recognised as an intrinsic (which it is), the value cannot stay in a double register across an operation boundary, so the JIT spills the bit pattern between them.

Everything downstream inherits it: comparisons (`EmitComparisonValue`), conditionals, and any arithmetic on a value that came from a double-producing call.

## Proposal A - type the ring slot (the 1.8x)

Make the ring's slot type follow the value's representation: an IEEE slot is `Variable(typeof(double), "_d{n}")`, not a `long`.

- `RingVar(int)` gains the slot's representation; the arms already know it (`RingCodec.IsIeee(node, ctx.Analysis)`).
- Arithmetic on two IEEE operands reads the `double` locals directly and assigns a `double` result slot: no `FromIeee`, no `ToIeee`.
- The bitcasts survive **only at boundaries where a word must move as a word**: the `_slots[FP+n]` frame, `VmCall.Invoke`'s result, and boxing into a heap handle. Those are per-call, not per-operation.

This is compatible with IS3 rather than a reversal of it: every `CompileNode` arm still leaves its result in a slot, and `CompileValue` stays deleted. The slot is simply typed, so a double is stored as a double.

**Risk:** mixed-representation expressions get more distinct locals, and a slot whose representation is not statically known still needs the `long` word form. Gate it on the FP benchmark plus the integer ones (Sieve is the sensitive one — it is where IS3's 38% came from, and typed slots must not erode it).

## Proposal B - one native FP baseline (do this first)

Every native entry in the suite is **fixed-point integer** mandelbrot. There is no C, C++, Rust or C# *double* mandelbrot, so the FP rows have nothing to be compared against — the honest statement today is only "Poly's double path is 2.1x its fixed-point path", which says nothing about competitiveness.

Add `mandelbrot_double.c` / `mandelbrot_double.cs` (same viewport mapping as `mandelbrot_polyvm_dbl.cs`, so the checksum matches 453734) and label them distinctly. Until that exists, any FP claim is unanchored.

## Proposal C - stop recomputing the guard (source-side, free)

`mandelbrot_polyvm_dbl.cs` evaluates `zx*zx + zy*zy` in the loop **guard** and `zx*zx - zy*zy` in the **body** — six multiplies per iteration where the integer programs do three. The native implementations compute `zx2`/`zy2` once and reuse them.

That is a benchmark defect, not an emitter one, and it inflates Poly's FP row against every other language. Fixing it also makes Proposal A's win measurable in isolation. A general CSE pass in the emitter would subsume it, but that is a much larger change and should be justified by its own measurement.

## Proposal D - smaller items, in order of expected value

1. **CSE within one expression compile.** `FromIeee(zx)` is emitted twice for `zx*zx`; a per-expression `node -> slot` memo removes the duplicate load and bitcast without touching the ring model.
2. **`float` never widens unnecessarily.** `RingCodec.FromClr` widens `float` to `double` before `ToIeee`. Correct for the ABI, but a `float`-typed computation could stay 32-bit within an expression.
3. **`decimal`.** Not on the ring at all today (heap handle); a workload using it is 10x+ off and will not be fixed by any of the above. Out of scope until something needs it.

## How to prove any of it

- `docs/perf-comparison` with Proposal B's native double baseline, both rows on the same side of the container boundary (see that README's environment section - the current table mixes host and container runs).
- `AstParityTests` (145 rows) for correctness; the double rows are already in its corpus thanks to IS2d's `double` literal fix.
- `VmTree.AssertInvariants`, extended with a slot-type rule: a slot declared `double` must never be read as a word without an explicit conversion.

## What not to do

- **Do not reintroduce a value form.** IS3 deleted `CompileValue` and measured Sieve at -38% because the in-place emitters were dead behind it. Proposal A gets the register win without a second answer to "how do I get a value out of a node".
- **Do not special-case `double` in the arithmetic arms.** The representation belongs on the slot, not in each operator, or the next numeric type repeats the problem.
