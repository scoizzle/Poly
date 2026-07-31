# DACR r3 review — 2026-07-30 (phenomenal, multi-pass)

- **Mode**: multi-pass phenomenal review per [`docs/agent/phenomenal-review.md`](../../../agent/phenomenal-review.md)
- **Pass A**: session agent (wrote the r2 slice) — full-context verification of each Pass B finding against current source + `git show HEAD`
- **Pass B**: fresh-context subagent (Explore), diff-only input from `/tmp/poly-dacr-diff.txt` (1140 lines)
- **Branch**: `rewrite/domainmodeling-from-scratch`
- **Scope**: r2 follow-up slice (F1–F14) on DACR helpers, runtime/MCP/lowering metadata-first routing, fail-closed tests, plan status
- **Diff stats**: 16 tracked files (+472/−159); 7 untracked (4 docs, 2 source, 2 skill wrappers)
- **Issue counts**: **1 bug, 3 suggestions, 5 nits**
- **Verdict**: **NOT done — B-1 blocks.** r2 work is largely sound and all F1–F14 are genuinely fixed in the tree (both passes verified), but B-1 re-opens the silent-no-op class that Stage-Action semantics (SA, Phase 3 §6e) exists to prevent, and the r2 disposition of r1-issue-8 must be corrected: the "fix" for that item IS this regression.

## Summary

This multi-pass review examined the F1–F14 follow-up slice. Pass B (fresh context, diff-only) produced the finding list; Pass A verified each against current source and the pre-change baseline (`git show HEAD`).

**Verified genuine (F1–F14, both passes):** fail-closed MCP describe routes (F4), exporter `ResolveRelationship` RLM throw (F5), `InvokeActionInternal` fallback-scan guard (F2), single-call `TransitionStage` analysis (F9), `NotifyTransition_NoThrow_WhenDomainIsNull` two-stage test (F1/F13), `DescribeAction` priority comment (F11), gate G5 1703/0 (F12), dead `?.` removal (F14). Build 0 errors / 0 warnings; tests 1703 passed / 0 failed.

**The one bug** is a regression introduced by the r1-issue-8 "fix": the SA empty-copy fallthrough predicate in `TryResolveAction` gained a `stageAction.Parameters.Count == 0` clause that was not in the legacy code. In the reachable mutation chain (AddAction → AddParameterToAction → AddActionToStage → AddStageTransitionEffect → runtime InvokeAction), a params-carrying stage copy no longer falls through to the entity action, producing the exact silent no-op SA semantics was created to prevent.

## Issues

### Issue 1 — Severity: bug (B-1) — SA fallthrough narrowed by `Parameters.Count == 0`

- File: `Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs:57-64` (`TryResolveAction`)
- Legacy baseline (verified via `git show HEAD:Poly/DomainModeling/DomainEntityInstance.cs`, line 290):
  ```csharp
  if (action is not null && action.Effects.Count == 0 && action.Policies.Count == 0
      && entityAction is not null)
      action = entityAction;
  ```
- Current tree:
  ```csharp
  if (stageAction.Effects.Count == 0
      && stageAction.Policies.Count == 0
      && stageAction.Parameters.Count == 0   // ← regression: not in legacy predicate
      && arm.EntityActions.TryGetValue(actionName, out var entityActionOverride))
  ```
- Why it matters: a stage copy created by `AddActionToStageChange` carries the source action's **parameters** (`DomainChange.cs:574-599`: `new Action(Name, source.Result, source.Parameters.ToArray(), ...)`). When effects are later added entity-only (entity-first via `UpdateAction(searchStages: true)` — `DomainChange.cs:216`, `DomainChange.cs:365`, `DomainMutationContext.cs:112-136`), the stage copy legitimately has `Parameters.Count > 0` but `Effects.Count == 0`. The new predicate fails the fallthrough for exactly this case; the runtime then invokes the stale stage copy → **silent no-op**.
- Full reachable scenario (all via MCP DSL surface):
  1. `AddAction("Task", "Submit")` → empty entity action
  2. `AddParameterToAction("Task", "Submit", ...)` → entity action gains params
  3. `AddActionToStage("Task", "Draft", "Submit")` → stage copy inherits params
  4. `AddStageTransitionEffect("Task", "Submit", "Active")` → entity-level effect only
  5. runtime `InvokeAction("Submit")` on instance in `Draft` → NEW code returns stage action (params ≠ 0) → **no transition, no error**
- Test coverage gap: the two SA tests (`AddActionToStage_CopiesEntityActionEffects` McpSmokeTests.cs:1550, `AddActionToStage_Order_StageBeforeEntityEffects_StillTransitions` McpSmokeTests.cs:1733) only cover the zero-params shell path. No test combines `AddParameterToAction` + `AddActionToStage` + runtime invoke (13 `AddParameterToAction` usages in `DomainEvolutionApplicatorTests.cs` — none in this combination).
- Resolution options (for follow-up F15): (a) drop `Parameters.Count == 0` to restore legacy behavior, or (b) keep it deliberately and add a test + comment documenting the deviation. Option (a) is the smallest fix that restores SA semantics.
- Note: r2 review's disposition table row "r1 Issue 8 — SA semantics drift — Fixed (`Parameters.Count` check added)" is **incorrect as a disposition** — the check is the regression. Corrected in the table below.

### Issue 2 — Severity: suggestion (S-1) — `EffectLoweringPass.StageTransition` fallback unguarded

- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:120-122, 139-143`
- The source/target stage fallback scans are guarded only by `if (sourceStage is null)` / `if (targetStage is null)`, not by `_analysis is null`. When `_analysis` is non-null but `TryGetStage` fails, lowering trusts the structural scan, while the runtime path (same diff) skips the scan (`if (prevStage is null && analysis is null)` in `TransitionStage`, F9). Generated code and runtime can disagree on what effects run.
- Suggestion: guard both scans with `&& _analysis is null` to match the F2/F9 pattern.

### Issue 3 — Severity: suggestion (S-2) — stage-guard policies skipped fail-open

- File: `Poly/DomainModeling/DomainEntityInstance.cs` (`InvokeActionInternal`, stage-guard loop `if (stage is not null)`)
- When `runtimeAnalysis` is non-null and `TryGetStage` fails (entity's stage not resolvable through ESM), `stage` stays null and the stage's policy guards are **silently skipped** — the action proceeds without stage-level guards. This is the opposite of the fail-closed posture formalized in the same method (throws/not-found on unresolvable action metadata).
- Reachability is narrow (requires analysis present + ESM lookup miss) but the asymmetry is real and contradicts the diff's own fail-closed contract.
- Suggestion: fail loud (throw) or explicitly document why a stage-guard lookup miss degrades open.

### Issue 4 — Severity: suggestion (S-3) — new fail-closed contract surface largely untested

- Files: `Poly.Mcp/Tools/OracleTool.cs` (F4 routes), `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:1270-1277` (F5), `Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs` (helpers)
- No tests cover: the F4 not-found paths in `DescribeStage`/`DescribeAction`/`DescribePolicy`/`DescribeRelationship` (OracleToolTests only exercises success paths), `DescribeAction`/`DescribeRelationship` at all, the F5 RLM throw in `ResolveRelationship`, or any of the `DomainSemanticLookupExtensions` helpers (`TryResolveAction`, `GetEffectivePolicies`, `TryGetRelationship`, `TryGetEntity`, `TryGetStage` — zero direct unit tests in `Poly.Tests`).
- Suggestion: add fail-closed coverage mirroring `DomainInstanceStoreFailClosedTests.cs` (strip metadata via `GetMetadataStore().Remove<T>(key)`; assert `Throws` / not-found).

### Issue 5 — Severity: nit (N-1) — gate evidence log undercounts fallback markers

- File: `docs/plans/simple-agent-tasks/dacr-gate.md:49-52`
- Gate says `DomainEntityInstance.cs (8 tags)`, `DomainMutationContext.cs (1 tag)`, `EffectLoweringPass.cs (fallback tag)`; actual counts in tree: DomainEntityInstance = 10, DomainMutationContext = 5, EffectLoweringPass = 7, DomainToCSharpExporter = 10 (unlisted), OracleTool = 4 (unlisted), MinimalApiGenerator = 3 (unlisted) — ~36+ total vs gate's "~28+".
- Suggestion: refresh the G2 evidence counts.

### Issue 6 — Severity: nit (N-2) — gate "Remaining risks" still lists F11–F14 as open

- File: `docs/plans/simple-agent-tasks/dacr-gate.md:59`
- "Open follow-ups: ./dacr-followups-2026-07-30.md (F11–F14 — suggestions/nits, not blocking)" — but F11–F14 are marked `[x]` closed in that file.
- Suggestion: update the line to point at the new r3 follow-ups.

### Issue 7 — Severity: nit (N-3) — double `GetOrAnalyze` in `CreateChildInstance`

- File: `Poly/DomainModeling/DomainEntityInstance.cs:707, 746`
- `CreateChildInstance` calls `RuntimeAnalysisCache.GetOrAnalyze(Domain)` twice (entity resolution, then relationship linking) — contradicts F9's single-call pattern in `TransitionStage`. Harmless via cache, inconsistent.
- Suggestion: hoist to one call and reuse.

### Issue 8 — Severity: nit (N-4) — `DescribePolicy` returns anonymous `Data` type

- File: `Poly.Mcp/Tools/OracleTool.cs:703, 723`
- `DescribePolicy` returns `Data: new { kind = "policy", ... }` while the other three describe routes return `DomainElementData`. Inconsistent tool-output contract.
- Suggestion: unify on `DomainElementData`.

### Issue 9 — Severity: nit (N-5) — ESM throw message conflates absent-ESM with stage-less entity

- File: `Poly/DomainModeling/DomainInstanceStore.cs:152-155`
- Message says `"Runtime dispatch requires EntityStructureMetadata for subscriber entity ..."` but `EntityStructureAnalyzer.cs:69-82` sets `StageByName = null` when the entity legitimately has no stages. A stage-less subscriber with a stale `CurrentStage` would get a misleading message. Reachability is narrow (needs stale instance) but the wording is wrong for that case.
- Suggestion: disambiguate "metadata absent" from "entity has no stages".

## Disposition of r2 findings (F11–F14)

All four are **genuinely fixed** in the tree — verified by both passes:

| r2 Issue | r2 Severity | r3 Status | Evidence |
|---|---|---|---|
| 11. DescribeAction priority | suggestion | **Fixed** | Comment at `OracleTool.cs:591-595` documents entity-first choice |
| 12. Gate G5 count | suggestion | **Fixed** | `dacr-gate.md` G5 → "1703 passed, 0 failed"; Remaining risks updated |
| 13. Test name misleading | nit | **Fixed** | Renamed `NotifyTransition_NoThrow_WhenDomainIsNull` |
| 14. Dead `?.` in DescribeStage | nit | **Fixed** | Explicit `StageCapabilityMetadata? stageCap = null;` + invariant comment |

## Disposition of r1 findings (corrections)

The r2 review table marked **r1 Issue 8 (SA semantics drift) as Fixed** ("`Parameters.Count` check added"). This is now **re-opened as B-1**: the check is a behavioral regression, not a fix. All other r1 rows stand as dispositioned.

| r1 Issue | r1 Severity | Status |
|---|---|---|
| 1–7, 9–13 | (as r2 table) | Unchanged |
| **8. SA semantics drift** | suggestion | **Re-opened → B-1 (bug)** — `Parameters.Count` check narrows the fallthrough; silent-no-op regression |

## Follow-ups

New tasks F15–F23 appended to [`./dacr-followups-2026-07-30.md`](./dacr-followups-2026-07-30.md):

- **F15** (bug, blocking): B-1 SA predicate — restore legacy effects+policies-only check OR keep + test + comment.
- **F16** (suggestion): S-1 — guard `EffectLoweringPass` fallbacks with `&& _analysis is null`.
- **F17** (suggestion): S-2 — fail loud or document stage-guard policy skip.
- **F18** (suggestion): S-3 — tests for F4 not-found paths, F5 RLM throw, `DomainSemanticLookupExtensions` helpers, `DescribeAction`+`DescribeRelationship`.
- **F19–F23** (nits): N-1 marker counts, N-2 gate stale wording, N-3 single `GetOrAnalyze`, N-4 `DomainElementData` unification, N-5 ESM message disambiguation.

## Review gate note

Per protocol §4 this was a review-only pass: no production or test code modified. The follow-ups doc is the system of record; the next "full send on the follow-ups" request resolves F15–F23 (harden mode).
