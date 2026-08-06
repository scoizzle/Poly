# DACR follow-ups — 2026-07-30 (review r2)

Parent: ./dacr-README.md
Gate: ./dacr-gate.md
Source reviews:
- `./dacr-local-review-2026-07-30.md` (r1 — early dirty tree; most items fixed)
- Latest local `/review` r2 (scratch notes folded into this file; do not leave follow-ups only outside `docs/`)

Status: [~] r5 code F24–F31 closed; F32/F33/F35 done (F33 closed via DAS W4.3 EffectLowering fail-closed, 2026-07-31); F34 optional hygiene open
Difficulty: Small residual (F34 DescribeStage nit only)

## Agent rule

All follow-up work from reviews of this suite **must** land as checked tasks in this file (or reopened phase/gate checkboxes). When an item is done, mark it here and note the fix in the owning phase file. Do not leave residuals only in chat or outside `docs/plans/`.

## r2 summary (current tree)

Much improved vs r1: MTI is domain-keyed, `GetEffectivePolicies` is entity+stage only, `DescribeStage` prefers `StageCapabilityMetadata`, `NotifyTransition` fails closed on missing `EntityStructureMetadata`, and fail-closed tests strip metadata for real. Residual risks: incomplete MCP describe fail-closed / policy coverage, SA missing on action **fallback** scan, a false-positive notify test, exporter “fail-closed” overclaim, and plan status still outrunning residual `DM-META-REMOVE-FALLBACK` work.

## Pick order

1. Bugs F1–F3 (correctness / false-positive coverage).
2. Contract tightening F4–F5 (MCP + exporter fail-closed when analysis present).
3. Plan hygiene F6–F8 (status, gate evidence, test name drift).
4. Nits F9–F10 when touching those files.

## Follow-up tasks

### Bugs

- [x] **F1** — Fix false-positive null-domain notify test  
  File: `Poly.Tests/DomainModeling/Analysis/DomainInstanceStoreFailClosedTests.cs`  
  Two stages + distinct transition (Domain still null); asserts no-dispatch early-return.  
  Phase note: P4/P6 test coverage.

- [x] **F2** — Fail closed on action **fallback** scan when analysis ran  
  File: `Poly/DomainModeling/DomainEntityInstance.cs`  
  When `runtimeAnalysis` is non-null, skip scan entirely — trust TryResolveAction/TryGetStage result (F2).  
  Phase: P4 residual / P6 fallback removal.

- [x] **F3** — `DescribePolicy` must use full MTI policy maps  
  File: `Poly.Mcp/Tools/OracleTool.cs` (`DescribePolicy`)  
  Now searches entity, stage, and action MTI maps with scope disambiguation.  
  Phase: P2 acceptance for policy completeness.

### Contract / fail-closed

- [x] **F4** — MCP describe routes: analysis-present must not soft-scan on missing metadata  
  File: `Poly.Mcp/Tools/OracleTool.cs` (`DescribeStage` / `DescribeAction` / `DescribePolicy` / `DescribeRelationship`)  
  When `LatestAnalysis` is non-null, all describe routes return not-found instead of falling through to structural scan.  
  Phase: P2 + P6.

- [x] **F5** — Exporter subscription path: throw when analysis present but RelationshipLookupMetadata absent  
  File: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` (`ResolveRelationship`)  
  Throws `InvalidOperationException` when analysis present but RLM is null.  
  Phase: P1/P6.

### Plan / gate hygiene

- [x] **F6** — Align P6 status with residual AC  
  File: `dacr-p6-contract-enforcement.md`  
  Status [~], progress notes list F1–F10 resolution with residual fallback-tagging noted.

- [x] **F7** — Update gate G3 evidence to real metadata set  
  File: `dacr-gate.md`  
  G3 updated: throw sites are RCM, ESM, SDPM, RLM, not ARM.

- [x] **F8** — Sync plan progress notes to real test method names  
  Files: `dacr-p6-contract-enforcement.md`, `dacr-p4-runtime-static-dynamic.md`  
  Names match actual methods in `DomainInstanceStoreFailClosedTests.cs`.

### Nits (batch when touching the file)

- [x] **F9** — `TransitionStage`: single `GetOrAnalyze` reused for OnExit/OnEntry  
  File: `Poly/DomainModeling/DomainEntityInstance.cs`

- [x] **F10** — `GetOutboundRelationships` / `GetInboundRelationships` document soft-empty  
  File: `Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs`  
  XML doc now notes no consumer yet requires fail-closed behavior.

## r1 disposition (historical)

| r1 issue | Disposition |
|---|---|
| MTI keyed with `default` | Fixed in tree (domain key) — do not re-open unless regression |
| DescribeStage 0 policies / over-aggregate action policies | Fixed (`StageCapabilityMetadata` / entity+stage only) |
| NotifyTransition silent skip on missing stage metadata | Fixed (ESM throw) |
| Fail-closed tests never strip metadata | Fixed (F1 + later fail-closed suite) |
| Scratch RestApi / SQLite / MidstreamOrderEntry / `dumb` poly | Removed from dirty tree; leave out of DACR commits |
| Premature “complete” status | Still open → **F6**, **F7** |

## Done definition (this follow-up slice)

1. [x] F1–F3 fixed with tests green.
2. [x] F4–F5 fixed with accurate comments/plan wording.
3. [x] F6–F8 docs match production contracts and test names.
4. [x] Gate G2/G3 and suite Done Definition still honest about residual `DM-META-REMOVE-FALLBACK` markers.

---

## r3 (2026-07-30 phenomenal review)

**Source**: `dacr-local-review-2026-07-30-r2.md`

**Status**: [x] Closed
**Verdict**: 0 bugs, 2 suggestions, 2 nits. r2 is ship-ready for its scope. No block-and-fix items.

### Suggestions

- [x] **F11** — DescribeAction should use TryResolveAction priority  
  File: `Poly.Mcp/Tools/OracleTool.cs` (`DescribeAction`)  
  Entity-first search priority matches structural fallback but diverges from `TryResolveAction` (stage-first + SA fallback). Pre-existing design asymmetry; documented with comment explaining design choice.

- [x] **F12** — Gate G5 test count out of date  
  File: `docs/plans/simple-agent-tasks/dacr-gate.md`  
  Evidence updated to "1703 passed, 0 failed". Remaining risks section also updated.

### Nits

- [x] **F13** — Test name `NotifyTransition_FailClosed_WhenStoreHasNoAnalysis` misleading  
  Renamed to `NotifyTransition_NoThrow_WhenDomainIsNull`.
  
- [x] **F14** — `DescribeStage` fallback path dead `?.` on analysis  
  Replaced with explicit `null` + comment explaining guard invariant.

---

## r4 (2026-07-30 phenomenal review, multi-pass)

**Source**: `dacr-local-review-2026-07-30-r3.md`

**Status**: [x] Closed — all F15–F23 resolved (2026-07-30).
**Verdict**: 1 bug, 3 suggestions, 5 nits. F1–F14 verified genuinely fixed; B-1 (F15) was the sole blocking bug — fixed by restoring the legacy SA predicate (no `Parameters.Count` check) plus a regression test.

### Bug (blocking)

- [x] **F15** — SA fallthrough predicate narrowed by `Parameters.Count == 0` → silent no-op regression  
  File: `Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs:57-64` (`TryResolveAction`)  
  **Fixed**: removed `stageAction.Parameters.Count == 0` — restores the legacy predicate (Effects+Policies only) with a comment explaining stage copies inherit parameters from the entity action via `AddActionToStageChange` (Phase 3 §6e).  
  **Test added**: `AddActionToStage_WithParameters_EntityEffectStillFallsThrough` (McpSmokeTests.cs) exercises the full chain AddAction → AddParameterToAction → AddActionToStage → AddStageTransitionEffect → runtime InvokeAction, asserting the transition happens (no silent no-op). Unit-level regression: `TryResolveAction_ParamCarryingStageCopy_FallsThroughToEntityAction` in `DomainSemanticLookupFailClosedTests.cs`.

### Suggestions

- [x] **F16** — `EffectLoweringPass.StageTransition` fallback scans unguarded by `_analysis is null`  
  File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:120-122, 139-143`  
  **Fixed**: both source/target fallback scans now guarded with `&& _analysis is null`, matching the F2/F9 runtime pattern.

- [x] **F17** — Stage-guard policies skipped fail-open when `TryGetStage` misses with analysis present  
  File: `Poly/DomainModeling/DomainEntityInstance.cs` (`InvokeActionInternal`)  
  **Fixed**: when `runtimeAnalysis is not null && CurrentStage is not null` and `TryGetStage` fails, `InvokeActionInternal` now throws `InvalidOperationException` (fail loud) instead of silently skipping stage-guard policies.

- [x] **F18** — Add tests for the new fail-closed contract surface  
  File: `Poly.Tests/DomainModeling/Analysis/DomainSemanticLookupFailClosedTests.cs` (NEW, 23 tests)  
  **Added**: F5 RLM throw + not-found + found paths for `ResolveRelationship`; helper unit tests for `TryGetStage` / `TryResolveAction` (incl. SA fallthrough and the B-1 params-carrying case) / `GetEffectivePolicies` / `TryGetRelationship` / `TryGetEntity`; F4 not-found paths for all four describe routes (strip metadata via `GetMetadataStore().Remove<T>`); describe success coverage for `DescribeAction` and `DescribeRelationship` (previously zero tests).

### Nits (batch when touching the file)

- [x] **F19** — Gate G2 evidence undercounts fallback markers  
  File: `docs/plans/simple-agent-tasks/dacr-gate.md:49-52`  
  **Fixed**: counts refreshed to actual (DomainEntityInstance = 10, DomainMutationContext = 5, EffectLoweringPass = 7, DomainToCSharpExporter = 10, OracleTool = 4, MinimalApiGenerator = 3; ~36 total), new test file listed.

- [x] **F20** — Gate "Remaining risks" lists F11–F14 as open  
  File: `docs/plans/simple-agent-tasks/dacr-gate.md:59`  
  **Fixed**: now references F15 (blocking) → resolved, F16–F18, F19–F23.

- [x] **F21** — Double `GetOrAnalyze` in `CreateChildInstance`  
  File: `Poly/DomainModeling/DomainEntityInstance.cs:707, 746`  
  **Fixed**: single hoisted `analysis` call reused for entity resolution + relationship linking, matching F9's `TransitionStage` pattern.

- [x] **F22** — `DescribePolicy` returns anonymous `Data` type  
  File: `Poly.Mcp/Tools/OracleTool.cs:703, 723`  
  **Fixed**: both `DescribePolicy` returns now use `DomainElementData` (with optional `Expression` field), matching the other three describe routes.

- [x] **F23** — ESM throw message conflates absent-ESM with stage-less entity  
  File: `Poly/DomainModeling/DomainInstanceStore.cs:152-155`  
  **Fixed**: split into two throws — metadata absent vs. entity has no lifecycle stages (clearer message for `EntityStructureAnalyzer`'s `StageByName = null` case).

## Done definition (r4 slice)

1. [x] F15 fixed (predicate restored + regression test) — B-1 closed.
2. [x] F16–F18 applied with tests green (1726+ total, 0 failed).
3. [x] F19–F20 docs match tree; F21–F23 applied when touching those files.
4. [x] Gate G2/G3 and suite Done Definition still honest about residual `DM-META-REMOVE-FALLBACK` markers.

---

## r5 (2026-07-30 phenomenal review, multi-pass) — code closed (r6 re-verify)

**Source**: `dacr-local-review-2026-07-30-r4.md`  
**Re-verify**: `dacr-local-review-2026-07-30-r6.md`

**Status**: [x] Code items closed (r6 primary evidence). Docs pick-order lag → r6 F32–F33.

### Disposition of r5 items (r6 re-verified against current source)

| Item | Disposition | Evidence |
|---|---|---|
| F24 (B-2 SA scan path) | **Fixed** | `DomainEntityInstance.cs:274-286` SA predicate; `InvokeAction_Standalone_EmptyStageCopyWithParams_FallsThroughToEntityAction` |
| F25 (ESM-miss posture) | **Fixed (documented asymmetry)** | Dispatch throws ESM-absent vs stage-not-found split; TransitionStage / NotifyTransition comments document best-effort skip |
| F26 (present-but-soft create/link) | **Fixed** | TryGetEntity/Relationship miss throws with analysis present |
| F27 (dead null check) | **Fixed** | Only `!TryGetValue` → continue |
| F28 (marker total) | **Fixed as 34** | Live greps sum to 34; gate G2 matches (not 39 — DomainEntityInstance now 5 tags) |
| F29 (gate Remaining risks) | **Partial** | Gate cleaned; README pick-order still F24-blocking → **F32** |
| F30 (ARM.StageByName) | **Fixed** | Record is `(EntityActions, StageActions)` only |
| F31 (protocol lessons) | **Fixed** | `docs/agent/phenomenal-review.md` §3.2a/b, §3.7.1, §3.8, §3.9 |

### Bugs / suggestions / nits (historical r5 text)

- [x] **F24** — SA fallthrough on analysis-absent scan path  
- [x] **F25** — ESM-miss posture documented / messages split  
- [x] **F26** — Present-but-soft create/link scans removed  
- [x] **F27** — Dead `subscriberStage is null` removed  
- [x] **F28** — Marker total corrected (now 34)  
- [x] **F29** — Gate remaining risks cleaned (README lag remains → F32)  
- [x] **F30** — `ActionResolutionMetadata.StageByName` removed  
- [x] **F31** — Phenomenal-review protocol amended  

## Done definition (r5 slice)

1. [x] F24 fixed (legacy SA predicate + standalone regression test).
2. [x] F25–F26 aligned or documented.
3. [x] F27–F30 applied (F29 README still lagged → r6).
4. [x] Gate G2/G3 honest about residual markers (34); suite green claimed at 1728.

---

## r6 (2026-07-30 phenomenal-review)

**Source**: `dacr-local-review-2026-07-30-r6.md`

**Status**: [~] F32/F33/F35 closed; F34 optional hygiene open  
**Verdict**: 0 bugs, 2 suggestions, 2 nits. No blocking runtime/MCP defects found on re-verify. F32–F33/F35 closed; F34 remains optional.

### Suggestions

- [x] **F32** — Align README + follow-ups status with closed F24–F31  
  Files: `dacr-README.md:14,30`, this file’s header/status  
  **Done (r6)**: pick order + status table no longer advertise F24 as blocking.

- [x] **F33** — G2 dual-path / fallback claims honest  
  File: `dacr-gate.md` G2  
  **Closed (DAS W4.3 re-open fix 2026-07-31):** `EffectLoweringPass.GetConstructorParameterOrder` fail-closed under analysis (ESM required); markers `*.cs` = 0; G2 + DACR item 4 closed. Evidence: [`das-gate.md`](./das-gate.md) G4.2 + [`das-w4-3-marker-zero-and-dacr-close.md`](./das-w4-3-marker-zero-and-dacr-close.md).

### Nits

- [ ] **F34** — `DescribeStage` null-analysis path dead `stageCap?.`  
  File: `Poly.Mcp/Tools/OracleTool.cs:571-573`

- [x] **F35** — r1 disposition row still said F1 open  
  File: this follow-ups file historical table — fixed in r6 edit.

## Done definition (r6 slice)

1. [x] F32 README/follow-ups pick order honest; [x] F33 G2 wording closed (EffectLowering fail-closed).
2. [ ] F34 optional hygiene; [x] F35 historical table fixed.
3. [x] Suite Done Definition item 4: markers 0 + analysis-present dual paths removed (DAS W4.3).
