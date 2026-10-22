# TASK IS6 - one owner per analysis fact

Status: planned. Branch `slice/IS6`. Lane B (`analysis`). Depends on IS0f.
Plan: [IS-README](../IS-README.md). Audit: [interpretation-simplification.md](../../../analysis/interpretation-simplification.md) §6 and its research limits.

## Why

The interpretation pipeline registers 13 passes in a fluent chain (`Poly/Interpretation/Interpreter.cs:20-34`) and several facts have more than one author:

| Fact | Authors | Hazard |
|---|---|---|
| Elision | `SideEffectAnalysisPass:48` **and** `ControlFlowAnalysisPass:69,675,717` | two writers, one reader |
| Purity polarity | `IsPure` (`ControlFlow:678-681`) and `HasSideEffects` (`SideEffect:107-111`) | **two copies of one answer** — see the correction below |
| Binding lookup | `IsBoundUse`, `BindsTo`, `BindingOf` (`TypeAndMember:59,90`; `SyntaxTypeCompatibility:517`) | three re-derivations |
| Constant booleans | `IsConstantTrue`, `TryGetConstBool`, `GetConstValue`, folding | four null conventions |

Plus three facts with **no production reader**: `IsDefinitelyAssigned` (`DefiniteAssignmentAnalyzer:175`), `IsInfiniteLoop` (`ControlFlow:831`), `IsMustExecute` (`ControlFlow:840`). Tenet 7: guardrails only with real consumers.

## Scope

**Elision — one producer, both reasons.** The audit's correction holds: CFG establishing "unreachable statement" and side-effect analysis establishing "safe-to-elide expression" are different reasons and both are legitimate. Unify the *author*, not the reason: one pass writes `ElisionMetadata`, the other contributes a reason record. Precedence must be written down and tested before any default changes — an unreachable statement and a side-effect-free expression are not interchangeable.

**Purity — one predicate, no behaviour change.** Checked at plan time against `9522d536`: the two predicates are **exact complements**, not opposites. For a non-null node both reduce to `metadata is not null && Kind == Pure`; for `null`, `IsPure` returns `true` and `HasSideEffects` returns `false`, which is the same complement. The audit's "opposite defaults" line reads a null node as missing metadata. So this is pure de-duplication: delete `IsPure`, call `!HasSideEffects`, and prove the equivalence with a characterisation test over the corpus that is green **before** and after. The card's original "red first" requirement does not apply because there is no behaviour to change — say so in the Log.

**Binding lookup — one derivation.** One helper answers "what does this use bind to"; `TypeAndMemberResolver` owns it and `SyntaxTypeCompatibility` calls it. Three names for one question is three places to disagree about shadowing.

**Constant booleans — one convention.** One `bool?`-returning query (`TryGetConstBool` shape: `null` = not known). Delete the conflating predicate and route every consumer through it and through constant folding, so "unknown" is never silently `false`.

**Unread facts — find the consumer or delete.** `IsDefinitelyAssigned`, `IsInfiniteLoop`, `IsMustExecute` are read by nothing in `Poly/` or `Poly.Tests` (`grep` recorded in the Log). Delete them, and the shallow-walk bug #258 exposed lives nowhere. If a consumer appears during implementation, stop and report — that changes the card.

**Not in this slice:** declared pass preconditions and a dependency scheduler. The audit defers that until an interpretation-pipeline dependency inventory exists; `PassDependencyDeclarationTests` covers a different pipeline.

## Files

- `Poly/Interpretation/Analysis/Semantics/SideEffectAnalysisPass.cs`
- `Poly/Interpretation/Analysis/ControlFlow/ControlFlowAnalysisPass.cs`
- `Poly/Interpretation/Analysis/Semantics/TypeAndMemberResolutionPass.cs`
- `Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityPass.cs` (or wherever `SyntaxTypeCompatibility:517` lives)
- `Poly/Interpretation/Analysis/Semantics/DefiniteAssignmentAnalyzer.cs`
- `Poly.Tests/Interpretation/*Analysis*`, `*ControlFlow*`, `*SideEffect*`, `*Const*`
- New: `Poly.Tests/Interpretation/AnalysisFactOwnershipTests.cs`

## Shape matrix

| Fact | Before | After | Test that proves it |
|---|---|---|---|
| purity with missing metadata | two implementations | one (`!HasSideEffects`) | `IsPureAndHasSideEffects_AreExactComplements` (green before and after) |
| elision written by two passes | last writer wins | one writer, reasons preserved | `BothElisionReasons_ArePreserved_AndPrecedenceIsRecorded` |
| binding lookup | three helpers | one | `BindingLookup_HasOneAnswerPerUse` |
| constant boolean | four null conventions | one `bool?` | `ConstBool_UnknownIsNull_NotFalse` |
| `IsDefinitelyAssigned` | no reader | absent | `grep` in the Log |

## Done when

- One author per fact, and the reason records survive the unification.
- The purity de-duplication is proven equivalent by a characterisation test, not by a behaviour change.
- The three unread facts are gone, with the `grep` that justified each deletion.
- No pass was reordered; the pipeline's order and behaviour are unchanged.
- Full suite green.

## SHIP if / NOT SHIP if

SHIP: each fact has one owner, purity equivalence is proven, and `grep` shows the unread facts' deletion.
NOT SHIP: either elision reason was dropped to make the unification simple; the pipeline was reordered; used facts were deleted along with unread ones; preconditions/scheduler were attempted.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AnalysisFactOwnershipTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*ControlFlow*/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*SideEffect*/*'
```
Then the full suite.

## Hand-edit

Each fact is written by one pass and read through one query; the precedence rule for elision is a sentence next to the writer.

## Needs Scot

One decision: **elision precedence.** CFG's "unreachable" and side-effect analysis's "safe to elide" are different reasons, and one writer must combine them. The recommended rule is: either reason may mark a node elidable, but a node CFG marks *must-execute* is never elided on the strength of the side-effect reason alone, and both reasons are recorded on the metadata so a reader can see why. Confirm or replace this before the unification lands; the rest of the card is behaviour-neutral.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
