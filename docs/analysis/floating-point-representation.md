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

## Typed local slots: what each representation buys

Two different justifications, and they do not apply to the same set.

**Speed.** Only `double` and `float` gain. They are the only primitives whose representation differs from the machine word, so they are the only ones paying a conversion per operation. Measured 1.8x.

**Semantics.** Typed locals make overflow structural rather than conventional. If the ring slot is `int _r3` and `Add` is emitted as `Expression.Add(intA, intB)`, the CLR performs a 32-bit add and wraps at 32 bits - no analysis pass, no convention, no way for the emitter to disagree with itself. Today the ring is `long`, so `Int32` arithmetic silently widens and only the constant folder narrows, which is [#289](https://github.com/scoizzle/Poly/issues/289).

That second argument covers **every** numeric primitive, and retires a class of silent divergence. The objection raised earlier in this note - "an `int` slot would add a widening `Convert` at every use" - is only true of a half-typed design where the slot is typed and the arithmetic is not. A fully typed chain adds no conversions at all: `int + int` stays `int + int`. **That objection was wrong and is withdrawn.**

### Where conversions do remain, and at what frequency

`SlotLocation` (`Poly/Interpretation/Vm/Abi/SlotLocation.cs`) already separates the four storage kinds, which is what makes this tractable:

| Location | Type after this change | Conversion |
|---|---|---|
| `Ring` - transient `_rN` locals | **typed** (`int`, `long`, `double`, ...) | none; this is where the win and the semantics live |
| `Frame` - `_slots[_fp + n]` | stays `long` word | one per read/write |
| `Cell` - `long[1]` captures | stays `long` word | one per read/write |
| `Local` - `_regN`, instance handle | stays as today | none |

So the change is **per-expression, not per-ABI**: everything inside one expression tree is typed and free; each crossing into a frame slot, capture cell, or call result costs one conversion. That is per statement or per call, not per operation.

**The residual cost is parameters.** A parameter lives in a frame slot, so a hot loop reading an `int` parameter converts on every read. Typing frame slots is a separate and much larger change (a monomorphic `long[]` cannot hold three widths; it would need per-width arrays or a byte-addressed arena). Measure whether any real workload is parameter-bound before attempting it - the ring change should land first and be measured on its own.

### The failure mode moves, and gets better

With an untyped ring, an analysis failure yields 64-bit arithmetic *pretending* to be 32-bit - silent, wrong, and invisible. With a typed ring, an analysis failure yields either the wrong declared width (a type-inference bug, which is what the analysis already owns and what its tests assert) or, if the type is genuinely unknown, a decision the emitter is forced to make explicitly: reject, or fall back to the word type and abandon the guarantee.

That is the same trade IS0a made for the writer - fail closed rather than fold to a wrong shape. A `long`-only ring folds every `Int32` to a wrong shape by construction.

### What this does to #289

Typed slots do not by themselves fix the folder, which computes in host arithmetic at compile time. But they *decide* the rule the folder must follow: **the declared type is the arithmetic width**. With the emitter typed, the folder's obligation becomes concrete and testable ("fold at the type's width") instead of a choice between two conventions. Fix #289 in the same change that types integer slots, not before - otherwise the rule has nowhere to be observed.

## Proposal A - type the ring slots

Make the ring's slot type follow the value's representation: an IEEE slot is `Variable(typeof(double), "_d{n}")` and an `Int32` slot is `Variable(typeof(int), "_i{n}")`, not a `long` in either case.

- `RingVar(int)` gains the slot's representation; the arms already know it (`RingCodec.IsIeee(node, ctx.Analysis)`).
- Arithmetic reads the typed locals directly and assigns a result slot of the operation's type: no `FromIeee`, no `ToIeee`, and an `Int32` add wraps at 32 bits because that is what `Expression.Add(int, int)` does.
- The arithmetic arms (not just the slot declaration) must be built at the slot type; a typed slot beside a 64-bit `Add` gains nothing.
- The bitcasts survive **only at boundaries where a word must move as a word**: the `_slots[FP+n]` frame, `VmCall.Invoke`'s result, and boxing into a heap handle. Those are per-call, not per-operation.

This is compatible with IS3 rather than a reversal of it: every `CompileNode` arm still leaves its result in a slot, and `CompileValue` stays deleted. The slot is simply typed, so a double is stored as a double.

**Order.** `double`/`float` first: it carries the measured 1.8x and no semantic question. Integer widths second, together with [#289](https://github.com/scoizzle/Poly/issues/289), because typed slots are what makes the width rule observable.

**Risk:** mixed-width expressions get more distinct locals, and a slot whose representation is not statically known still needs the `long` word form. Gate it on the FP benchmark plus the integer ones (Sieve is the sensitive one — it is where IS3's 38% came from, and typed slots must not erode it). "Correct by construction" is not a licence to skip the measurement.

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
