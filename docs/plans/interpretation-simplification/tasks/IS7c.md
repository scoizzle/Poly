# TASK IS7c - integer width is a representation, and analysis supplies it

Status: implemented, in review. Branch `slice/IS7c`. Lane C (`vm`), with a lane-B analysis dependency. Depends on IS7b.
Plan: [IS-README](../IS-README.md). Bug: [#289](https://github.com/scoizzle/Poly/issues/289).

## Why

`Int32` arithmetic currently has two answers. The constant folder narrows (`int.MaxValue + 1` → `-2147483648`); the emitter evaluates at 64 bits and never narrows (the same sum through variables → `2147483648`). Neither path is wrong in isolation - they disagree.

IS7a/IS7b built exactly the mechanism that fixes this class: a value's representation is part of its storage, so the *machine* performs the arithmetic at the declared width. Typing integer slots makes the width structural instead of conventional. `Expression.Add(int, int)` wraps at 32 bits because that is what the CLR does - no convention to keep in sync, and the folder's obligation becomes concrete ("fold at the type's width") instead of a choice.

The "via analysis" half is the same point from the other end: the representation must come from the resolved type, not from a guess. Where analysis cannot name the width, the emitter must not silently pick 64-bit - that is how #289 happened.

## Scope

**Two integer representations, not ten.** C# promotes every smaller integral type to `int` before arithmetic (`short + short` is `int`, `char + char` is `int`, `bool` does not participate). So the only arithmetic widths that exist are `int` and `long`. Introduce `int` alongside the existing word (`long`); leave `short`/`byte`/`char`/`bool` as words whose *arithmetic* is `int` because C# says so.

1. **A `int` representation** in the ring and register slot tables built by IS7a/IS7b, alongside `long`, `double`, `float`. Same (index, representation) keying, so the alias test extends rather than changes.
2. **The width comes from analysis.** `RingCodec`/`SlotTypeOf` currently answer "is this IEEE?"; add "is this a 32-bit value?" sourced from the resolved type (`PrimitiveType.Int32` is already distinguished from `Int64` at `TypeAndMemberResolutionPass.cs:355-357`, and a constant carries its width in its boxed CLR type - `Constant(1)` is `int`, `Constant(1L)` is `long`).
3. **Unknown width is not silently 64-bit.** Decide and record:
   - **Preferred:** a compile rejection naming the node, matching `RejectCompile`'s shape. Try this first and measure the blast radius.
   - **Fallback:** keep the word and add an invariant test that fails if any corpus program reaches integer arithmetic through an unresolved width. Do not adopt this without the rejection measurement - fail-closed is the house rule (`AGENTS.md` §C# surface).
4. **The folder folds at the resolved width**, so `int.MaxValue + 1` gives the same answer folded and unfolded. This is the actual bug fix.
5. **Assignment and comparison at the same width.** Narrowing an `int` result into an `int` slot is a no-op; a `long` into an `int` truncates unchecked, which is C#'s default context; a comparison of two ints is an int compare.

## Files

- `Poly/Interpretation/Vm/Abi/RingCodec.cs` - the representation question.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Context.cs` - the slot/register tables.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.cs` / `.Expressions.cs` / `.Statements.cs` - arithmetic, comparison, assignment arms.
- `Poly/Interpretation/Analysis/ConstantFolding/` - fold at the width.
- `Poly.Tests/Interpretation/*` + `Poly.Tests/TestHelpers/VmTree.cs`.
- `docs/interpretation/vm-abi-reference.md`.

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| `Add(Constant(int.MaxValue), Constant(1))` | `-2147483648` (folded, 32-bit) | `-2147483648` | `Int32MaxPlusOne_FoldedAndUnfolded_Agree` |
| same through **variables** | `2147483648` (64-bit) | `-2147483648` | the same test, second half |
| `long` addition at the same magnitude | does not wrap | does not wrap | `Int64Addition_DoesNotWrap` |
| `short`/`char` arithmetic | 64-bit | promoted to `int`, wraps at 32 | `SmallIntegerArithmetic_PromotesToInt32` |
| an int variable read into a long expression | no conversion | one conversion, value preserved | `Int32ToInt64_WidensOnce` |
| an int result assigned to a long slot | no narrowing | widens | `Int32Result_IntoLongSlot_Widens` |
| integer width unknown to analysis | 64-bit, silently | rejects (preferred) or is counted by an invariant | `UnknownIntegerWidth_IsNotSilently64Bit` |

## Done when

- Both halves of #289's reproduction return the same `RawValue`.
- The parity corpus gains the case, so `AstParityTests` covers a genuine unfolded `Int32` overflow rather than only folded constants.
- **Blast-radius report:** this changes runtime results for any existing program that relied on 64-bit arithmetic behind an `Int32` declaration. Run the full suite and list every test whose expected value changed, with its reason. **A silent expectation edit is a NOT SHIP.**
- Benchmarks (`--job medium`): Sieve, Collatz, NQueens, Mandelbrot must all hold. Sieve is the sensitive one - it is full of `int` loop counters, so this slice touches it most.
- Full suite green; `AstParityTests` 145/145; build 0 warnings.

## SHIP if / NOT SHIP if

SHIP: folded and unfolded agree, the parity corpus covers it, the blast radius is listed and justified, the four benchmarks hold.
NOT SHIP: a test expectation was changed without a reason; `long` lost its 64-bit semantics; small integral types wrap at their own width instead of promoting to `int` (that would be wrong C#); the unknown-width case is handled by luck.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*Int32*/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmTreeInvariantTests/*'
dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job medium
```

## Hand-edit

The width is one answer from analysis and one local declared with it; a reader can find the fold and the emit and see they read the same source.

## Needs Scot

One decision if the blast radius is large: whether programs that relied on the current 64-bit runtime behaviour should be fixed or the rule relaxed. The recommended position is that C# `int` semantics are the contract and such programs were relying on a bug - but the measurement comes first, and this card reports rather than decides if the number is uncomfortable.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-23 | agent + parent | Copilot/VS Code | (this commit) | **Both halves of #289 agree; blast radius zero.** `Int32` becomes a representation alongside the word: `RingCodec.IsInt32Width` covers `int` and every smaller integral type C# promotes to `int` before an operator (`short`/`byte`/`sbyte`/`ushort`/`char`); `uint` is excluded because 32-bit unsigned does not fit the signed `int` slot, so it keeps the word. `IntegerArithmeticWidth(left, right)` answers from the types analysis resolved: one `long`/`ulong` operand makes the operation 64-bit, two 32-bit operands make it `int` and the CLR wraps at 32. **Unresolved width throws** (`VM compile rejected: cannot resolve the integer width of an arithmetic operand`, naming both operands) - the card's preferred fail-closed route, not the word fallback. `ConstantFoldingPass` folds at the resolved width, which is the actual fix. | **RED (pre-change, plan-branch tip `face49ea`, measured by a probe before this slice existed):** `Add(Constant(int.MaxValue), Constant(1))` -> `RawValue = -2147483648` (folded, 32-bit) while the same sum through locals -> `RawValue = 2147483648` (emitter, 64-bit). **GREEN:** both halves return `-2147483648`; `Int32WidthTests` 6/6. A revert-only red is not demonstrable because the new tests call the new API (the build fails before a runtime assertion can run), so the probe above is the evidence. **Blast radius: zero.** Full suite **4512/4512** green with no expectation edited; the three test files touched are additive - a corpus row for the unfolded overflow (`AstParityTests`), `_i`/`_ireg` locals recognised by `VmTree`'s regexes, and one `DirectVmAbiEmitterTests` parameter given its declared type because an untyped operand now rejects instead of guessing 64-bit. **Benchmark `--job medium`, plan branch -> this branch:** NQueens 169.70 -> 169.68, Sieve 97.94 -> 97.25, Mandelbrot 68.39 -> 68.39, Collatz 136.14 -> 138.78 (StdDev 5.45, Min 132.31 - inside run-to-run noise, since the same program measured 132-155 across runs). Sieve, the most `int`-heavy program, holds. **Operational note:** an earlier benchmark run was reported as an infinite loop. It is not: a bounded re-run of the clean tree (155.9 s) and of this tree (153.6 s, plus a 1.15 s `--job dry`) both complete, and every benchmark program terminates in `GlobalSetup`'s checksum verification. The apparent hang was CPU contention during concurrent worktree builds. `Int32WidthTests` therefore carries `[Timeout(10_000)]` on every case, so a genuine non-termination fails instead of hanging the suite. |
