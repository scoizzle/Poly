# TASK IS3 - `CompileNode` is total, `CompileValue` is gone

Status: planned. Branch `slice/IS3`. Lane C (`vm`). Depends on IS2c, ISB.
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
- Benchmark: `Min`/`Median` for the four ISB programs, recorded next to the ISB baseline. **Tolerance: no median regression above 2% on any program.** A higher ring high-water is acceptable only if it is recorded with the measured cost.
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
