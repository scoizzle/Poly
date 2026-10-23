# TASK IS2c - delete the second interpreter

Status: **held** — depends on IS2b and IS2d. Branch `slice/IS2c`. Lane A (`csharp`).
Plan: [IS-README](../IS-README.md). Audit: [interpretation-simplification.md](../../../analysis/interpretation-simplification.md) §2.

## Held until the writer agrees with the VM

IS2a built the replacement differential and found 29 rows where the printed program disagrees with the VM — 11 writer defect classes, two of them silent wrong values ([#279](https://github.com/scoizzle/Poly/issues/279)). Those rows are pinned in `AstParity.KnownDivergences`, so **nothing verifies VM behaviour for those shapes today**. Deleting the LINQ oracle first would delete the only oracle for them as well.

This slice therefore starts when [IS2d](IS2d.md) has emptied `KnownDivergences`. That is the card's own precondition, restated as a dependency, not a new one.

## Why

`LinqExpressionGenerator` is 1,093 LOC in three files (`Poly/Interpretation/LinqExpressions/`) with **zero production references** — the only mention outside its own folder is a doc comment in `DomainExpressionLoweringPass.cs:23`. As an oracle it is unsound in both directions: it has fewer arms than the VM, it accepts `ParameterReference` that the VM rejects, it models `this` as `null`, and its harness skips exactly the interesting failure.

After IS2a there is a differential with an external authority (Roslyn) and after IS2b nothing depends on the in-house one. Deleting it removes one interpreter, one hand-kept switch, and the question "which of two interpreters is right?"

## Scope

- Delete `Poly/Interpretation/LinqExpressions/` (3 files, 1,093 LOC) and `Poly.Tests/Interpretation/LinqExpressionGeneratorTests.cs`.
- **`VmCorrectnessTests` is not ported — it is replaced.** Its 64 tests compare the VM to the LINQ oracle and assert **no independent expected value** (`await Assert.That(vmVal).IsEqualTo(linqVal)`). They cannot be ported; converting them to explicit expectations is a separate, larger piece of work. Delete the parity-only cases and show, in the Log, that every expression shape they covered has an `AstParity` (IS2a) row. If a case asserts something the parity harness does not cover, it moves to `AstParity` first — deleting coverage is not allowed.
- Remove `Poly.Tests/TestHelpers/NodeTestHelpers.cs`'s `BuildExpression*`/`CompileLambda` helpers if IS2b left them in place, and any `using Poly.Interpretation.LinqExpressions;`.
- Rewrite `Poly/Interpretation/README.md:9` — it currently presents the LINQ path as a semantic checker — and delete its entry at `:257`.
- Update the `DomainExpressionLoweringPass.cs:23` comment.
- Record the closure: issue evidence, the `-1,093 LOC` figure, and the parity coverage mapping.

## Files

- `Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.cs`, `.Helpers.cs`, `.ControlFlow.cs` — deleted
- `Poly.Tests/Interpretation/LinqExpressionGeneratorTests.cs` — deleted
- `Poly.Tests/Interpretation/VmCorrectnessTests.cs` — parity-only cases removed, coverage mapped
- `Poly.Tests/TestHelpers/NodeTestHelpers.cs`
- `Poly/Interpretation/README.md`
- `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` — comment only

## Shape matrix

| Item | Before | After | Test that proves it |
|---|---|---|---|
| `LinqExpressionGenerator` | 1,093 LOC, 33 test references | absent | `grep -r LinqExpressionGenerator` returns only this plan |
| `VmCorrectnessTests` parity cases | VM == LINQ | deleted, shapes covered by `AstParity` | the coverage mapping in the Log + `AstParityTests` green |
| an expression shape only LINQ covered | covered by two in-house interpreters | covered by VM + Roslyn | one `AstParity` row per shape |
| the interpreter README | documents a second interpreter | one interpreter plus Roslyn | doc read |

## Done when

- The folder, the test class, and every reference are gone.
- The coverage mapping is complete: no expression shape lost an assertion.
- `Poly/Interpretation/README.md` describes the pipeline that exists.
- Full suite green; the test count change is explained by the deleted parity cases.

## SHIP if / NOT SHIP if

SHIP: the deletion is total, the coverage mapping shows no lost shape, and the README matches the code.
NOT SHIP: a reference survives behind `#if`; a `VmCorrectnessTests` case is deleted without a mapped replacement; the README still mentions the LINQ path.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmCorrectnessTests/*'
```
Then the full suite.

## Hand-edit

There is one interpreter. Its oracle is a compiler in another package, named where the parity tests live.

## Needs Scot

Recommended and proceeding unless Scot objects: the 64 `VmCorrectnessTests` parity cases are deleted rather than converted to explicit expected values. They have no independent expectation today, so a "port" would be inventing 64 expectations, which is a different task with its own review burden. The coverage they exercised moves to `AstParity`, which has an external authority. If Scot wants the explicit-value versions, that becomes a follow-up card after IS2c.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
