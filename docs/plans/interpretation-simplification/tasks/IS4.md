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
| 2026-10-22 | agent | Copilot/VS Code | — | CHARACTERISATION: `LoopScaffoldShapeTests` written and green against the pre-refactor emitters | `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/LoopScaffoldShapeTests/*'` on the four hand-kept emitters → `total: 5 failed: 0 succeeded: 5`. New reader: `VmTree.Scan.Loops` / `VmTree.LoopShape` (`Steps`, `GuardIndex`, `BreakTestIndex`, `ContinueLabelIndex`, `ContinueTarget`, `InTryFinally`, and the `*Count` counterparts). The shapes it pinned, straight off the `:232`/`:269`/`:512`/`:574` code: `while` = guard, test-jump, body, continue label (4 steps); `for` = guard, test-jump, body, continue label, increment (5); `do` = guard, body, continue label, test-jump (4); `foreach` = guard, MoveNext test-jump, binding, body, continue label (5), all inside the enumerator's `try`. A loop's own `if (x) break;` cannot be mistaken for the scaffold's jump because the scaffold's test is always negated (`IfThen(Not(test), …)`). **Scope** is characterised by consequence, not by a field: `continue` in the body jumps to *this* loop's `ContinueTarget` (push happened before the body compiled), and a `continue` after the loop throws `break/continue outside an enclosing loop` (pop happened). Nothing in the tree exposes `PushLoopScope`, and inventing a field for it would have been a test of the test. |
| 2026-10-22 | agent | Copilot/VS Code | 93855540 | SHIP: `EmitLoopScaffold(LoopPlan, ctx)` is the one scaffolding; the four emitters are plans | **GREEN** with that same test file unchanged: shape filter `5/5`; `VmTreeInvariantTests` `6/6`; `(*ForEach*)` `20/20`; full suite `4327/4327` on the `7751704d` base and `4336/4336` after rebasing onto the plan tip `5c2bce21` (IS1d); `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` 0 warnings on both. **Both trees, one test file:** the final test file was also run with `AbiEmitter.Statements.cs` at its pre-refactor content (`git show 7751704d:…`) → `5/5` again, so the characterisation spans the change and was not rewritten to fit it. **Byte-identity:** a scratch fingerprint dump rendered `AbiEmitter.Build(…).Expression` as a full tree in `CompilationMode.Normal` for 12 programs (while, while+break/continue, do, do+continue, for, declared-`for`+continue, `for` with no condition, foreach, foreach+break, while nested in for, labelled while) and diffed before/after → `IDENTICAL`, including ring-local numbering and local declaration order. That is why the scaffold can compile `test, per-iteration, body, after-continue` for every plan while `for` used to compile body/increment/test: each compile starts from the loop's entry `RingDepth` inside its own `OpenRingScope`, so the order is not observable. The dump was a scratch test, deleted before the commit; it is not part of the PR. **What stays outside the scaffold, and why:** `do`'s test-after-body is `LoopTestPosition.AfterBody`, the only thing that makes the emitted test the last step; `for`'s `continue` lands on the increment because the increment is the plan's `AfterContinue` and the scaffold emits it *after* the continue label, while `do` has no `AfterContinue` so its continue label is followed directly by the test — the two placements read as `ContinueLabelIndex == Steps.Count - 2` versus `BreakTestIndex - 1` in the shape tests; `foreach`'s per-iteration binding is a `PerIteration` plan hook (the scaffold places it after the test, before the body), and its enumerator lifetime — collection slot, `GetEnumeratorOrThrow`, `TryFinally` + `IDisposable` dispose — stays in `EmitForEachLoop`, which is why the loop reads as `InTryFinally` and why the shape test pins **one** dispose for three iterations rather than merely 'disposed'; `for`'s declaration scope (`PushScope`, `DeclareVariables`, cell init, initializer) stays in `EmitForLoop` and wraps the scaffold call. **Ordering deviation, deliberate:** `for` now opens its variable scope *before* the loop scope (it used to be the reverse), because the variables must exist before the initializer compiles and the loop scope now starts inside the scaffold. Both are compile-time lookups with no interaction — `break`/`continue` consult only loop scopes, variables only variable scopes — and the emitted tree is byte-identical. No benchmark run: the card makes measurement optional and the tree is unchanged by construction. |
| 2026-10-22 | agent | Copilot/VS Code | — | CHARACTERISATION: `LoopScaffoldShapeTests` written and green against the pre-refactor emitters | `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/LoopScaffoldShapeTests/*'` on the four hand-kept emitters → `total: 5 failed: 0 succeeded: 5`. New reader: `VmTree.Scan.Loops` / `VmTree.LoopShape` (`Steps`, `GuardIndex`, `BreakTestIndex`, `ContinueLabelIndex`, `ContinueTarget`, `InTryFinally`). The shapes it pinned, straight off the `:232`/`:269`/`:512`/`:574` code: `while` = guard, test-jump, body, continue label (4 steps); `for` = guard, test-jump, body, continue label, increment (5); `do` = guard, body, continue label, test-jump (4); `foreach` = guard, MoveNext test-jump, binding, body, continue label (5), all inside the enumerator's `try`. A loop's own `if (x) break;` cannot be mistaken for the scaffold's jump because the scaffold's test is always negated (`IfThen(Not(test), …)`). **Scope** is characterised by consequence, not by a field: `continue` in the body jumps to *this* loop's `ContinueTarget` (push happened before the body compiled), and a `continue` after the loop throws `break/continue outside an enclosing loop` (pop happened). Nothing in the tree exposes `PushLoopScope`, and inventing a field for it would have been a test of the test. |
| 2026-10-22 | agent | Copilot/VS Code | afa63fd7 | SHIP: `EmitLoopScaffold(LoopPlan, ctx)` is the one scaffolding; the four emitters are plans | **GREEN** with that same test file unchanged: shape filter `5/5`; `VmTreeInvariantTests` `6/6`; `(*ForEach*)` `20/20`; full suite `4327/4327` on the `7751704d` base and `4336/4336` after rebasing onto the plan tip `5c2bce21` (IS1d); `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` 0 warnings on both. **Both trees, one test file:** the final test file was also run with `AbiEmitter.Statements.cs` at its pre-refactor content (`7751704d`) → `5/5` again, so the characterisation genuinely spans the change and was not rewritten to fit it. **Byte-identity:** a scratch fingerprint dump rendered `AbiEmitter.Build(…).Expression` as a full tree in `CompilationMode.Normal` for 12 programs (while, while+break/continue, do, do+continue, for, declared-`for`+continue, `for` with no condition, foreach, foreach+break, while nested in for, labelled while) and diffed before/after → `IDENTICAL`, including ring-local numbering and local declaration order. That is why the scaffold can compile `test, per-iteration, body, after-continue` for every plan while `for` used to compile body/increment/test: each compile starts from the loop's entry `RingDepth` inside its own `OpenRingScope`, so the order is not observable. The scratch dump was deleted before the commit; it is not part of the PR. **What stays outside the scaffold, and why:** `do`'s test-after-body is `LoopTestPosition.AfterBody`, the only thing that makes the emitted test the last step; `for`'s `continue` lands on the increment because the increment is the plan's `AfterContinue` and the scaffold emits it *after* the continue label, while `do` has no `AfterContinue` so its continue label is followed directly by the test — the two placements read as `ContinueLabelIndex == Steps.Count - 2` versus `BreakTestIndex - 1` in the shape tests; `foreach`'s per-iteration binding is a `PerIteration` plan hook (the scaffold places it after the test, before the body), and its enumerator lifetime — collection slot, `GetEnumeratorOrThrow`, `TryFinally` + `IDisposable` dispose — stays in `EmitForEachLoop`, which is why the loop reads as `InTryFinally` and why the shape test asserts one dispose for three iterations rather than merely 'disposed'; `for`'s declaration scope (`PushScope`, `DeclareVariables`, cell init, initializer) stays in `EmitForLoop` and wraps the scaffold call. **Ordering deviation, deliberate:** `for` now opens its variable scope *before* the loop scope (it used to be the reverse), because the variables must exist before the initializer compiles and the loop scope now starts inside the scaffold. Both are compile-time lookups with no interaction — `break`/`continue` consult only loop scopes, variables only variable scopes — and the emitted tree is byte-identical. No benchmark run: the card makes measurement optional and the tree is unchanged by construction. |
