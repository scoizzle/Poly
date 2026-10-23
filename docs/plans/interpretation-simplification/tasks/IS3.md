# TASK IS3 - `CompileNode` is total, `CompileValue` is gone

Status: implemented. Branch `slice/IS3`. Lane C (`vm`). Depends on IS2c, ISB.
Plan: [IS-README](../IS-README.md). Audit: [interpretation-simplification.md](../../../analysis/interpretation-simplification.md) §3 (and #260's deeper dive).

## Why

`CompileValue` (`Poly/Interpretation/Vm/Abi/AbiEmitter.cs:521-579`) is a second answer to "how do I get a value out of a node":

- **~20 arms are pure delegations** — `SpillRingRead(CompileNode(n, ctx), ctx)` and near-duplicates for `Constant`/captured `Variable`;
- **7 kinds are implemented twice** rather than delegated: `And`, `Or`, `Not`, `UnaryMinus`, `BitwiseNot`, `PopCount`, and comparison (`CompileCompareTest` versus `EmitComparisonValue`), each also reachable through `CompileConditionAsBool`.

So every arm is a fresh judgement — "does this need a value form?" — answered yes seven times, unnecessarily twenty times, and no for the rest. The correct shape already exists in the same file family: `EmitBinaryArithmetic` (`AbiEmitter.Expressions.cs:80`) is a one-line delegate to `EmitBinaryArithmeticValue`.

## Scope

- Make every `CompileNodeInner` arm (`:230-296`) leave its result in the ring; a statement or void arm leaves `0L`.
- Delete `CompileValue`. Its replacement is `SlotLocation.Ring(RingDepth - 1).Read` at each call site (`CompileNodeInner`'s `NullForgiving` arm and the twins).
- Delete the seven twins once their only caller is the value form: `EmitNotValue`, `EmitUnaryMinusValue`, `EmitBitwiseNotValue`, `EmitPopCountValue`, `EmitLogicalAndValue`, `EmitLogicalOrValue`, and the comparison twin. **Exception:** `EmitUnaryMinus`, `EmitBitwiseNot`, `EmitNot`, and the short-circuit forms reuse the operand's ring slot and assign in place (see ABI6's residual 4). Those are shape-distinct, not exact twins — keep the in-place form and say so in a comment; only delete the arm that existed solely for `CompileValue`.
- The void special case in `AbiEmitter.Invoke.cs` (#260) disappears by construction: the void arm leaves `0L` because every arm does. Remove IS0e's now-redundant local assignment only if the general rule demonstrably covers it; keep it otherwise and note why.
- **Cost is one ring slot** on kinds that currently compute directly into a value. Measure; do not assume.

## Files

- `Poly/Interpretation/Vm/Abi/AbiEmitter.cs` — `CompileNodeInner`, delete `CompileValue`, the value helpers.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Expressions.cs` — the twins and the in-place forms.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Invoke.cs` — only if IS0e's local assignment becomes redundant.
- `Poly.Tests/Interpretation/VmTreeInvariantTests.cs` — the ring rule.
- `Poly.Tests/Interpretation/SlotInvariantsTests.cs` — high-water expectations.
- `docs/interpretation/vm-abi-reference.md` — the ring high-water advice changes if the mark moves.

## Shape matrix

| Kind | Before | After | Test that proves it |
|---|---|---|---|
| a value kind (`Add`) | ring form + value form | ring only; callers read the slot | existing arithmetic/VmTests green |
| a void kind (`BreakStatement`, void `Invoke`) | no ring value | `0L` in the ring | `EveryCompileNodeArm_LeavesItsResultInTheRing` |
| `Not`/`UnaryMinus`/`BitwiseNot` in an in-place position | in-place reuse | unchanged (shape-distinct) | `ShapeTests` unchanged |
| `CompileValue` | exists | absent | `grep -r CompileValue` returns only this plan and the audit |

## Done when

- `CompileValue` is gone; every call site reads the ring slot.
- A structural test asserts the rule over the corpus: after a `CompileNode` arm, `RingDepth - 1` has been written (via `VmTree.Scan`). It fails when an arm forgets.
- Benchmark: `Min`/`Median` for the four ISB programs, recorded next to the ISB baseline. Run with `--job medium` (ISB proved `--job short` varies up to 2.3x on one tree). **Tolerance: no median regression above 2% on any program.** A higher ring high-water is acceptable only if it is recorded with the measured cost.
- `docs/interpretation/vm-abi-reference.md` matches the emitted shape.
- Full suite green.

## SHIP if / NOT SHIP if

SHIP: one value form, the structural rule test passes, and the benchmark is inside tolerance with both numbers in the Log.
NOT SHIP: a twin was left "temporarily"; the ring high-water grew with no measurement; a shape-distinct in-place form was collapsed into a spill.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmTreeInvariantTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SlotInvariantsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstParityTests/*'
```
Then the full suite, and the ISB benchmark command.

## Hand-edit

There is one function that answers "what does this node produce", and every arm ends the same way: a value in the ring.

## Needs Scot

Recommended and proceeding unless Scot objects: statements leave `0L` rather than nothing. The alternative — a void variant of the rule — reintroduces the second form this slice exists to delete.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-22 | agent | Copilot/VS Code | `7515cb3d` | SHIP — `CompileNode` is total (every arm leaves its result at `RingDepth - 1`, `0L` for a statement) and `CompileValue` is gone | **What was there.** `CompileValue` had 51 arms: **20 exact** `SpillRingRead(CompileNode(…))` delegations (`Constant`, captured `Variable`, `Default`, `This`, `TypeAs`, `TypeCast`, `Await`, `TypeOf`, `IndexAccess`, `Block`, `Member`, `New`, `NewArray`, `Parameter`, `Invoke`, `InterpolatedString`, `TypeIs`, `SwitchStatement`, `StridedSetBits`, `Assignment`), **3 near-delegations** (inline `Constant` → `Constant(val)` with no slot; uncaptured `Variable` → `VariableRead`; `NullForgiving` → recurse), **24 with a body of their own** (10 arithmetic, 6 comparison, `Not`/`UnaryMinus`/`BitwiseNot`/`PopCount`/`And`/`Or`, `Conditional`, `Coalesce`) and 4 rejection arms. **Deleted:** `CompileValue`, `SpillRingRead`, `EmitConstantValue`, `EmitIndexAccessValue`, `EmitNotValue`, `EmitUnaryMinusValue`, `EmitBitwiseNotValue`, `EmitPopCountValue`, `EmitLogicalAndValue`, `EmitLogicalOrValue`; `grep -r CompileValue` now finds only this plan and the audit. **Replacement:** `ValueOf(node, ctx)` = `Compile(node, ctx)` then `RequireResult(node).Read(ctx)` — the *reported* `SlotLocation`, not a re-derived index — at 17 call sites. **The in-place forms already existed and were dead:** the switch was spilling `EmitNotValue`… while `EmitNot`, `EmitUnaryMinus`, `EmitBitwiseNot`, `EmitPopCount`, `EmitLogicalAnd`, `EmitLogicalOr` sat unused; they are now the only form for their kind, each answering in the operand's own ring slot, and each says so in its comment. **Comparison:** `EmitComparisonValue` is now `Condition(CompileCompareTest(…), 1L, 0L)`, so the heap/IEEE/spill ladder exists once; the condition path keeps its bool shape, so `while (a < b)` gained no 0/1 materialisation. **Statements:** `Statement(ctx, code)` reserves the result slot *before* compiling (a delegate, so a branch's value cannot become the statement's answer) and writes `0L` after; 13 arms wrap. **Deliberately transparent** (no slot of their own, each commented): `Comment` (keeps the pending result — `[x, comment]` still yields `x`), `LabelDeclaration` (answers with its statement), `TryCatchFinally` (answers with its try block, which is how the classifier reads it — wrapping it broke `Try_WithoutCatchOrFinally_RunsBody`, 4 → 0), `Block`, `NullForgiving`. **#260 / IS0e's void special case survived, deliberately:** the rule is per *arm*, and the Invoke arm is the only place that knows the CLR target returns `void` — and the only place that reserved that slot. Nothing above it can write it, so deleting the local assignment would restore #260 exactly; same for the AST-invoke `isVoid` arm. **RED, before the implementation** (`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmTreeInvariantTests/EveryCompileNodeArm*'`): `total: 1 failed: 1` — `IfStatement, WhileLoop, DoWhileLoop, ForLoop, ForEachLoop, Return_Void, BreakStatement, ContinueStatement, GotoStatement, UsingStatement: the arm left no ring result`. **RED again on the finished tree** with `WhileLoop => EmitWhileLoop(…)` unwrapped — `WhileLoop: the arm left no ring result` (plus the two jump cases rooted in it) — then reverted: `7/7`. The test needs the root's result *location*, so `Build` now reports it as `BuiltVmProgram.RootResult` and the copy-out reads it instead of re-deriving `RingDepth - 1`; a whole-tree `VmTree.Scan` then proves that slot (and every other declared slot) is written. **GREEN:** `VmTreeInvariantTests` 7/7, `SlotInvariantsTests` 6/6, `AstParityTests` **145/145**, `LoopScaffoldShapeTests` 5/5 (its continue assertion now looks inside the statement wrap), `LambdaBridgeVmTests` 30/30 three times, full suite **4460/4460** twice in a row; `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` 0 warnings. **Benchmarks:** within tolerance — NQueens +0.5 %, Sieve −38.1 %, Mandelbrot −2.3 %, Collatz −1.2 % (medians of a back-to-back baseline/after pair, `--job medium`; table below). Sieve's win was not diagnosed further: the shape change is the only emitter difference and `GlobalSetup` verifies its checksum, so it is real — the likely cause is JIT register allocation in the one compiled delegate, which the extra ring local per operand perturbs. **Ring high-water:** the root body's `MaxRingDepth` is unchanged (NQueens 3, Sieve 3, Mandelbrot 4, Collatz 3 — measured with a throwaway probe on both trees and then deleted); these programs' hot code lives in its own compiled function delegates, whose ring counts `VmProgram` does not expose, so the cost is the end-to-end timing above rather than a recorded slot delta. **Flake, not this slice:** `LambdaBridgeVmTests.CapturingDelegate_SeesMutationAfterCreation` failed once in the third full run, then passed 30/30 three times in isolation and in two later full runs — `LambdaBridgeProbe._captured` is process-wide static state that sibling tests `Capture` into and `Fire`, exactly the interleaving IS0e recorded for `DelegateInvokedAfterExecution_StateAlive_SeesLiveCapture`. |

### Benchmark (2026-10-22, `7515cb3d`)

```
$ dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job medium
```

`Job=MediumRun  IterationCount=15  LaunchCount=2  WarmupCount=10`; Apple M3 Pro, .NET 10.0.12, Arm64 RyuJIT.

Baseline is this branch's HEAD `b22e7c59` (no change to the tree), measured twice: once before any edit and once back-to-back with the after run. The after column is the back-to-back run; three after runs agreed within their own spread.

| Method | Before Min | Before Median | After Min | After Median | Median Δ |
|--------|-----------|---------------|-----------|--------------|----------|
| NQueens | 164.7 us | 165.1 us | 163.9 us | 165.9 us | +0.5 % |
| Sieve | 154.9 us | 156.0 us | 96.1 us | 96.6 us | −38.1 % |
| Mandelbrot | 126.4 us | 128.0 us | 124.1 us | 125.1 us | −2.3 % |
| Collatz | 129.1 us | 135.9 us | 130.3 us | 134.3 us | −1.2 % |

Collatz is the only program whose median moves between runs beyond its min (its StdDev is ~3 us and BenchmarkDotNet drops outliers): its three after medians were 134.3, 137.9 and 135.1 us against two before medians of 135.0 and 135.9 us, i.e. −1.2 % to +1.5 %. Its min and mean are flat (129.1/136.0 before, 130.3/134.5 after). No program regressed by more than the 2 % tolerance.

The ISB baseline on `e0426fa5` read 165.9/166.7, 157.1/158.7, 136.0/140.1, 132.6/141.2 (Min/Median) — the same machine class, and consistent with the before column above.
