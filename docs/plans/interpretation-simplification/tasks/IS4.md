# TASK IS4 - one loop scaffold, four callers

Status: planned. Branch `slice/IS4`. Lane C (`vm`). Depends on IS1, IS0d.
Plan: [IS-README](../IS-README.md). Audit: [interpretation-simplification.md](../../../analysis/interpretation-simplification.md) §4.

## Why

Four loop emitters in `Poly/Interpretation/Vm/Abi/AbiEmitter.Statements.cs` — `EmitWhileLoop:232`, `EmitDoWhileLoop:269`, `EmitForLoop:512`, `EmitForEachLoop:574` — and **six of seven scaffolding steps are byte-identical**: the break/continue label pair, `PushLoopScope`, `EmitLoopIterationGuard`, `IfThen(Not(test), Goto(break))`, `Loop(body, break)`, `PopLoopScope` plus the `RingDepth` restore. The step that differs is the one the loop shape actually needs.

That is the class of bug #259 lives in: the order in which ring depth, labels, and scope pushes are emitted is decided four times, slightly differently, with nothing checking the four agree.

## Scope

- `EmitLoopScaffold(LoopPlan, ctx)` with `LoopPlan = (test: Before|After, perIterationPrelude, body, afterContinue)`. `while` uses neither hook; `for` passes the increment as `afterContinue`; `do` selects `After`; `foreach` passes `MoveNext` + binding as the prelude.
- **These stay distinct and outside the scaffold** — they are the loop's meaning, not its scaffolding: `do`'s test-after-body, `for`'s `continue` landing on the increment (`:546-547`) versus `do`'s landing on the test (`:270`), `foreach`'s per-iteration binding and enumerator lifetime/dispose (`:563-567,597-604`), and `for`'s declaration scope (`:510-514`).
- The scaffold owns the incidental: label names, ring save/restore order, scope push/pop, and the compile-then-rewind idiom — decided once.
- No behavioural change. The existing suite plus the characterisation test below is the oracle; if a loop's emitted tree changes in a way the suite does not explain, stop.

## Files

- `Poly/Interpretation/Vm/Abi/AbiEmitter.Statements.cs` — the scaffold and the four emitters.
- New: `Poly.Tests/Interpretation/LoopScaffoldShapeTests.cs`

## Shape matrix

| Loop | Meaning it keeps | Scaffolding it loses | Test that proves it |
|---|---|---|---|
| `while` | test before body | 6 hand-typed steps | `While_HasOneGuardAndOneScope` |
| `for` | `continue` → increment; own declaration scope | same 6 | `For_ContinueLandsOnIncrement` |
| `do` | test after body; `continue` → test | same 6 | `Do_ContinueLandsOnTheTest` |
| `foreach` | per-iteration binding; enumerator dispose | same 6 | `ForEach_DisposesEnumerator` |
| all four | — | one scaffold | `AllFour_ShareOneScaffoldShape` (asserts guard/label/scope ordering structurally via `VmTree.Scan`) |

## Done when

- One `EmitLoopScaffold`; the four emitters are plans passed to it, with no repeated `PushLoopScope`/label/guard sequence left in any of them.
- `LoopScaffoldShapeTests` is written against current behaviour first, then stays green through the refactor.
- `VmTree.AssertInvariants` passes for the loop corpus; no benchmark regression beyond noise (`ISB` baseline).
- Full suite green.

## SHIP if / NOT SHIP if

SHIP: the four scaffold-step sequences exist once and the shape tests pass unchanged.
NOT SHIP: a semantic difference was folded into the scaffold and a loop changed meaning; `do`/`for` continue placement moved; `foreach`'s dispose moved inside the scaffold.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/LoopScaffoldShapeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmTreeInvariantTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ForEachLoopTests/*'
```
Then the full suite.

## Hand-edit

Four short emitters read as a plan each; the order of labels, guard, and scope is in one function.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
