# Final Boss re-verify PR 66 — 2026-09-08

- **Target**: PR 66 https://github.com/scoizzle/Poly/pull/66 · branch `fix/constraint-lower-on-assign` · PINNED worktree `/workspace/Poly-pr66-a65aa50d` · vs `origin/master` and vs prior Razor ship-gate tip `a2241db145ed2df36d4e83e91dfe1427b4297591`
- **Mode**: re-verify (not rubber-stamp)
- **Model**: grok-4.6
- **SHA**: `a65aa50dce560fd62e4c5e69db3b1c821bf08bd8` (`git rev-parse HEAD` matched at review start; `gh pr view 66` `headRefOid` matched; HEAD not rewritten)
- **Issue counts**: 0 bugs, 4 suggestions, 1 nit (open after this pass; plus process P1)
- **Verdict**: **ship** — F1 closed at this SHA; no new ship-blocker on sibling paths
- **Process notes**: Tip delta vs `a2241db1` is 2 files. Null-guard is assign-only. Create-factory `IsMatch` remains bare; reachable on exported `Create` (demo `Patron.Create`) but pre-existing and not this tip. Do not reopen F1 as a Create-factory ship gate.

## Summary

Tip `a65aa50d` closes Razor F1. Pattern assign now emits `And(NotEqual(valueRef, null), Not(Regex.IsMatch(...)))` so a true-null RHS never reaches `IsMatch` (C# `&&`, VM `AndAlso` / `Expression.Condition`). `PatternAssign_Null_DoesNotThrow_RetainsPriorValue` proves export-shaped CS contains a null check plus `IsMatch`, and `InvokeAction` with `code: null` returns Failure with the pattern message and retains prior `"OK"` (no throw). That runtime Failure is bag Text `Convert.ToString(object null) → ""`, not the true-null soft-skip. True-null skip matches `ValidateConstraints` (`v is string`). F2–F5 / N1 remain suggestions/nit. No new dual-path bug found that belongs on the ship gate.

## PR facts (verified this session)

| Claim | Evidence |
|-------|----------|
| Tip SHA `a65aa50d…` | `git rev-parse HEAD`; `gh pr view 66` `headRefOid` |
| MERGEABLE / CLEAN | `gh pr view 66` → mergeable MERGEABLE, mergeStateStatus CLEAN |
| Diff vs `origin/master` | **2 files**: `EffectLoweringPass.cs` (+233/−17), `ConstraintAssignLoweringTests.cs` (+347) |
| Diff vs `a2241db1` | **2 files only**: `EffectLoweringPass.cs` (+15/−6), `ConstraintAssignLoweringTests.cs` (+37) |
| Single fix commit on tip | `a65aa50d fix: PR 66 Razor F1 null-safe pattern assign` |
| No Notify / StoreBind / PR 62 drift in tip | `git diff --name-only a2241db1...HEAD` = those 2 files |

## Checklist

- [x] Diff collected vs `origin/master` and vs prior ship-gate tip `a2241db1`
- [x] Stance: adversarial re-verify; Razor claims treated as claims, not facts
- [x] F1 walk from `git show a65aa50d:` + current tree (IR, C# emit, VM short-circuit, InvokeAction bind, ValidateConstraints)
- [x] Sibling-path check before severity (assign / ValidateConstraints / Create factory / analysis helper / void export / length / unique order)
- [x] Reachability argued for Create-factory `IsMatch` and void-export throw
- [x] F2–F5 / N1 re-read at this SHA; not chain-trusted
- [x] Oracles: ConstraintAssign `[Test]` count **11**; full suite **2827** succeeded, **0** failed (`dotnet run --project Poly.Tests -p:NuGetAudit=false`)
- [x] Review + follow-ups written; Razor files not overwritten

## F1 disposition — **CLOSED**

**Prior claim:** assign pattern emitted bare `Regex.IsMatch(valueRef, pattern)`; true-null RHS → `ArgumentNullException` instead of Failure / skip.

**This SHA** (`git show a65aa50d:Poly/DomainModeling/Lowering/EffectLoweringPass.cs`, current `EffectLoweringPass.cs:424-438`):

```csharp
// Null RHS: skip IsMatch (align ValidateConstraints: only check when string).
nodes.Add(new IfStatement(
    new Syntactic.And(
        new NotEqual(valueRef, new Constant(null)),
        new Syntactic.Not(
            new Invoke(
                new Member(
                    TypeReference.To<System.Text.RegularExpressions.Regex>(),
                    "IsMatch"),
                [valueRef, new Constant(p.Pattern)]))),
    new Block([Fail(
        $"'{prop.Name}' does not match the required pattern.")])));
```

| Required prove | Evidence this SHA |
|----------------|-------------------|
| Null never reaches `IsMatch` in the assign tree | Guard is `And(NotEqual null, Not IsMatch)`. C# emit: `&&` (`CSharpGenerator.cs:1112-1113`). VM If: `CompileConditionAsBool` → `AndAlso` (`DirectVmAbiEmitter.cs:620-621`; `EmitIfStatement` at `DirectVmAbiEmitter.Statements.cs:208-210`). VM value-And: `Expression.Condition` skips RHS (`DirectVmAbiEmitter.cs:601-604`). |
| No throw on InvokeAction null Text | `PatternAssign_Null_DoesNotThrow_RetainsPriorValue` (`ConstraintAssignLoweringTests.cs:117-152`) is in the green 2827; asserts `Succeeded == false`, message contains `does not match the required pattern`, `Code` still `"OK"`. |
| Prior retained | Same test: `GetProperty<string>("Code") == "OK"`. Fail-before-mutate: assignment is appended after the If (`WrapConstrainedAssign` `EffectLoweringPass.cs:317-333`). |
| Export CS has null guard | Handmade lower + `CSharpGenerator`: asserts `!= null` or `is not null` **and** `IsMatch` (`ConstraintAssignLoweringTests.cs:133-141`). `NotEqual` prints ` != ` (`CSharpGenerator.cs:1097-1098`). |
| Align ValidateConstraints true-null | `DomainEntityInstance.cs:201-203`: `if (v is string ps && !Regex.IsMatch(ps, pc.Pattern))`. True null is not `string` → skip. Assign If short-circuits the same way. |

**Failure vs soft-skip (recomputed, not copied):**

- **True null in the lowered tree:** `valueRef != null` is false → pattern If body skipped → no Failure → later `Assignment` still runs. That is create-time `v is string` skip, not a throw.
- **InvokeAction `code: null`:** args land in the bag (`DomainEntityInstance.cs:521-525`); `BindModuleMethodBody` rewrites action params to `Member(entity, name)` (`:859-867`, `:896-897`). Dictionary Text read goes through `DictionaryBackedValue.CoerceRead` → `Convert.ToString(object)` (`TypeDefinitionNodeAnalyzer.cs:456-498`, `GuardCompatible` `:509-530`). This runtime: `Convert.ToString((object)null)` is `""` (not null). `"" != null` is true; `^[A-Z]+$` fails on empty → **Failure**, prior retained. Never `ArgumentNullException`.
- The new null-guard is therefore **not** the InvokeAction oracle’s reason for no-throw; bag empty-coercion is. The guard is the export / true-null path. Test comment at `:143-144` states this. Not a ship-blocker: tree is one shape; DEI bag bind is scratch (CORE).

**Create-factory sibling (not F1 reopen):** `DomainToCSharpExporter.Notify.cs:433-446` still emits bare `IsMatch(paramRef, pattern)`. Reachable: generated `demo/Poly.RestApi/Patron.cs:130-131` `Patron.Create(..., string email, ...)` calls `Regex.IsMatch(email, ...)` with no null guard. Valid C# `email: null` → `ArgumentNullException`. `ValidateConstraints` would skip. Pre-existing, **not** in the 2-file tip delta, **not** a new assign-path bug. Residual → P1. Severity stays suggestion (reachable on export Create, not on the assign tree this tip fixed).

## Sibling-path check

| Path | Pattern-null / constraint semantic | Test forces this path? |
|------|------------------------------------|------------------------|
| Assign lower `AppendAssignConstraintChecks` (`EffectLoweringPass.cs:424-438`) | Null-guarded `IsMatch`; true null skips Failure | Handmade CS null-check (`ConstraintAssignLoweringTests.cs:133-141`). InvokeAction null is bag-`""` Failure, not true-null skip. |
| Module action body (`session.Lower` / `LowerActionToMethodBody` `DomainToCSharpExporter.Actions.cs:424-449`) | Same assign helper; `ActionResultType` set → `DomainResult.Failure` not throw | `PatternAssign_Mismatch_IsFailure` `:92-115`; `PatternAssign_Null_…` InvokeAction `:145-151`; `ModuleBody_AndExport_…` range only (`:209-239`) |
| ValidateConstraints (`DomainEntityInstance.cs:201-203`) | `v is string` then `IsMatch`; true null skips | Create-time; not an assign test. Alignment is source, not a new e2e. |
| Create factory (`Notify.cs:433-446`) | **Bare `IsMatch`** — null throws | No test in this PR. Demo `Patron.cs:130-131` shows the emit. Residual. |
| Analysis `ConstraintValidation.IsValidPattern` (`ConstraintValidation.cs:65-72`) | `value is not string` → **false** (unsatisfied), never calls `IsMatch` on null | Analysis-time literals; different layer (fail vs skip). |
| Void export assign Failure (`EffectLoweringPass.cs:447-451`) | `UseThisReference && ActionResultType is null` → `throw InvalidOperationException` | **No** OnEntry/ctor test (F3). Reachable: ctor first-stage entry (`DomainToCSharpExporter.cs:618-630`); export `OnEntry{Stage}` (`RuntimeAnalysisCache.cs:253-267`). Execute binds **VM-shaped** `LowerStageBatchVm` (`:238-250`, UseThis false → Failure) then `ThrowIfEffectListFailed`. Loud fail both sides; mechanism differs. |
| Optional length, no Required (`EffectLoweringPass.cs:410-417`) | Assign skips null/empty | `Length_WithoutRequired_EmptySucceeds_TooShortFails` `:178-206`. Create `ValidateConstraints` `:193-198` and factory `Notify.cs:413-430` still fail empty. F2. |
| Unique after constraints (`EffectLoweringPass.cs:317-332`) | Checks then `EnsureUnique` then assign | Unique-only regression tests `:242-270`. No unique+range/pattern combo (F5). |
| Range / required assign | Fail-before-mutate; messages match Create templates | `InvokeAction_RangeOutOfBounds_…` `:67-90`; `RequiredTextAssign_Empty_IsFailure` `:272-293`; `Length_WithRequired_EmptyFails` `:154-176` |

No new dual-path introduced by the tip: pattern assign is still one helper with a null conjunct. Create factory was already a second emitter.

## Prior Razor claims — prove or reject

| Razor claim | This pass |
|-------------|-----------|
| F1 CLOSED | **Prove** — IR + short-circuit + test + ValidateConstraints `v is string` |
| Top bugs none | **Prove** — no new reachable wrong outcome on the assign tree |
| F2–F5 still suggestions | **Prove** — source unchanged by tip except pattern If; length/throw/oracle/unique lines still as cited |
| N1 still nit | **Prove** — hardcoded required→range→length→pattern (`:353-438`) vs Create declaration order |
| Do not block ship on F2–F5 | **Agree** — no new bug found that upgrades them |
| Create factory residual only if reachable | **Reachable** (Patron.Create email); still suggestion, not ship-blocker |

## Adversarial focus results

### 1. F1 null-guard — **PASS (closed)**

Assign IR guards `IsMatch`. Export CS prints `&&`. VM short-circuits. InvokeAction null does not throw; prior retained; Failure message present under bag `null→""`. True-null skip aligns `ValidateConstraints`.

### 2. Void-export throw — **no new bug**

Throw is reachable on valid domains with constrained assign in ctor/OnEntry/subscription export (`EffectLoweringPass.cs:447-451`). Runtime execute uses VM-shaped Failure then `ThrowIfEffectListFailed`. Outcome is loud fail, not silent success. Missing test = F3 suggestion.

### 3. Optional length empty-skip vs create — **no new bug**

Assign AC (skip empty without Required) vs Create fail-empty is the same split Razor F2 named. Tip did not change `:410-417`.

### 4. Unique after constraints — **no new bug**

Order is constraints → EnsureUnique → assign (`:317-332`). Unique-only still wraps when `HasAssignableConstraints` is false (`:268-277`, Unique excluded from that helper). Missing combo e2e = F5.

### 5. Dual-path create vs assign — **no new ship-blocker**

Create factory pattern still throws on null; assign does not. Tip did not copy the factory. Documented residual (P1). Length empty-skip remains assign-only (F2).

## Issues

### Issue 1 -- Severity: suggestion
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:410-417` (vs `DomainEntityInstance.cs:193-198`, `DomainToCSharpExporter.Notify.cs:413-430`)
- Description: Optional length (no Required) skips null/empty on **assign**. Create-time `ValidateConstraints` and Create factory still fail `""` on `length(min,…)`. Same property, different gates.
- Suggestion: Document assign-vs-create in the DSL guide, or teach Create/`ValidateConstraints` the same `!Required ⇒ skip null/empty` length rule.
- Status: open (carried F2)
- Reachability: valid domain `Text length(7,20)` without `required`; assign `""` succeeds (`ConstraintAssignLoweringTests.cs:178-196`); create `""` fails length.

### Issue 2 -- Severity: suggestion
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:447-451`
- Description: Void export contexts (`UseThisReference && ActionResultType is null`) throw `InvalidOperationException` on assign constraint Failure. No test forces ctor/OnEntry/subscription sibling. Runtime OnEntry uses VM-shaped Failure (`RuntimeAnalysisCache.cs:238-250`) then `ThrowIfEffectListFailed`.
- Suggestion: Export/ctor or OnEntry lowering test: constrained assign fails → throw (or Failure, matching the bound body) and destination not assigned after.
- Status: open (carried F3)
- Reachability: valid domain, first-stage `entry { assign … }` of a constrained property to a violating value; ctor emit at `DomainToCSharpExporter.cs:618-630`.

### Issue 3 -- Severity: suggestion
- File: `Poly.Tests/DomainModeling/Lowering/ConstraintAssignLoweringTests.cs:209-239` (soft ORs `:33`, `:63`)
- Description: Handmade “export” pass omits `ActionResultType` → throw-shaped tree. Soft `must be >= || Failure` does not prove Failure-shape parity with `LowerActionToMethodBody`. `ModuleBody_AndExport_*` asserts `must be >=` on both sides but the export pass still lacks `ActionResultType` (`:229-235`).
- Suggestion: Build export via `DomainToCSharpExporter.LowerActionToMethodBody` (sets `ActionResultType`) and assert Failure + `must be >=` before assign on both module and export CS.
- Status: open (carried F4)

### Issue 4 -- Severity: suggestion
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:317-332`
- Description: Unique-after-constraints order is correct; no e2e unique+range/pattern InvokeAction test.
- Suggestion: One property Unique+Range (or Pattern); out-of-range fails before mutate without a peer; in-range duplicate hits EnsureUnique Failure and leaves prior value.
- Status: open (carried F5)

### Issue 5 -- Severity: nit
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:353-438`
- Description: Assign hardcodes required→range→length→pattern. Create/`ValidateConstraints` walk declaration order (`Notify.cs:367+`, `DomainEntityInstance.cs:176-177`).
- Suggestion: Match declaration order or document fixed precedence as intentional.
- Status: open (carried N1)

## Oracle

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false
total: 2827, failed: 0, succeeded: 2827, skipped: 0
```

`ConstraintAssignLoweringTests` has **11** `[Test]` methods (grep this SHA), including `PatternAssign_Null_DoesNotThrow_RetainsPriorValue`, `InvokeAction_RangeOutOfBounds_FailsAndLeavesPriorValue`, `Length_WithRequired_EmptyFails`, `Length_WithoutRequired_EmptySucceeds_TooShortFails`. Isolated `--treenode-filter` for that class ran 0 tests (TUnit tree depth); a 5-level filter did not isolate and executed the full 2827, all green.

## What still holds

- Item 6 assign lowers required/range/length/pattern before mutate; RHS once via `assignValueN`.
- Optional length empty-skip on assign (AC); unique EnsureUnique after constraint checks.
- Happy-path Failure messages match Create / ValidateConstraints templates.
- No Notify/StoreBind drift in tip delta.
- CI/PR: MERGEABLE, CLEAN at review time (checks not re-watched).
