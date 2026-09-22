# PR 75 Final Boss re-verify — 2026-09-22

- **Target**: PR #75 (`refactor/session-compile-slice-b-lower-at-lower`) tip `0c5b58eebfd102144828a6c9c0d785e5090a3a3b` vs `origin/master` (`20e35a4c`; 5 files, +306/−70)
- **Mode**: re-verify (not rubber-stamp). Razor claims in `docs/agent/reviews/2026-09-22-pr75-0c5b58ee-razor.md` treated as hypotheses proved or rejected from **this SHA**.
- **Reviewer**: Final Boss (executor hands)
- **Model**: grok-4.6 / executor hands OK
- **SHA**: `0c5b58eebfd102144828a6c9c0d785e5090a3a3b` (`git rev-parse HEAD` matched; product files not rewritten)
- **PINNED worktree**: `/workspace/Poly-pr75-0c5b58ee`
- **Issue counts**: 1 bug, 3 suggestions, 1 nit (same set as Razor F1–F5; no upgrade)
- **Verdict**: **ship** — Slice B stop condition **MET**. Execute never `LowerActionBody` on Runtime hot paths; Interpreter binds GetOrLower module / EntryExit / segment / policy bodies; Domain-null ExecuteEffectList / EvaluatePolicy fail closed. CI Build & test **SUCCESS**; mergeable **CLEAN**. Close F1 (lying Domain-null subscription comment) preferred for honesty; not a stop fail / fail-open.
- **CI at review**: Build & test SUCCESS; `headRefOid` = tip; `mergeable=MERGEABLE`, `mergeStateStatus=CLEAN` (`gh pr view` this process).
- **Out of scope**: C/E, DEI delete, Item 5, PR 73, CURRENT / PIPELINE-STATUS / plans docs.

## Summary

Re-verified Razor at pinned tip with primary greps and reads. Stop holds: `allowExecuteTimeLower` is gone; Runtime has zero `LowerActionBody(` call sites (only GetOrLower-time in `RuntimeAnalysisCache` `:321`, `:417`); Domain-bound ExecuteEffectList / RunTransitionEffectList / EvaluatePolicy / Domain-bound subscription bind cache and miss-throw; Domain-null ExecuteEffectList (`:775-777`) and EvaluatePolicy (`:383-385`) throw. SliceB Clear(segment) / Clear(policy) oracles are hard no-relower proofs.

Razor F1 confirmed as **honesty bug**, not fail-open: HostAbi `:330` still says Domain-null “keep execute-time lower path” while `ExecuteEffectList` always throws Domain-null — the Domain-null subscription arm allocates a lowering pass then dies at the throw. F2–F5 stand as suggestion/nit; no severity upgrade (no fail-open found). PR74 F1 UseThis:false HostAbi comment remains **honest / CLOSED**.

## Stop-condition disposition

| Claim | Disposition | Evidence (this SHA) |
|---|---|---|
| `rg allowExecuteTimeLower` → 0 in Poly + Poly.Tests | **MET** | Recomputed: **0** hits |
| `rg 'LowerActionBody\(' Poly/DomainModeling/Runtime` → 0 | **MET** | Recomputed: **0** in Runtime; only `RuntimeAnalysisCache.cs:321` (`BuildStageBatchMethodExport`) and `:417` (`BuildSubscriptionCaches`) at GetOrLower |
| Domain-bound hot paths bind GetOrLower / EntryExit / segments / policy; miss throws | **MET** | Named action `:717-720`; segment `:731-735`; EntryExitBodies `:740-766`; Domain-bound no-stage throw `:769-772`; EvaluatePolicy `:389-392`; Domain-bound subscription `:311-314` |
| Domain-null ExecuteEffectList / EvaluatePolicy fail closed (throw), no silent re-lower | **MET** | ExecuteEffectList `:775-777`; named-action Domain-null `:712-714`; EvaluatePolicy `:383-385`. No `LowerActionBody` in either method body |
| SliceB ClearEntryExitSegment / ClearPolicy throw fail-closed (hard oracles) | **MET** | `SliceB_ClearEntryExitSegment_ThrowsFailClosed_NoRelower` clears seg0 then `TransitionStage("Mid")` → IOE containing `"segment"`; `SliceB_EvaluatePolicy_UsesCachedBody_ClearThrowsFailClosed` ClearPolicy → `"missing"`; Domain-null EvaluatePolicy oracle also present |
| Interpreter only runs lowered / cached module bodies on claimed paths | **MET** | ExecuteEffectList ends at `Interpreter.CompileChecked` + `Execute` on bound cache trees (`:782-787`); EvaluatePolicy same (`:395-397`); Domain-bound subscription `ExecuteCachedSubscriptionTree` (`HostAbi:346-348`) |
| Mixed flush segment index matches GetOrLower cache | **MET** | Producer `CacheEntryExitSegments` `:283-303` and consumer `RunTransitionEffectList` Flush `:224-236` share nonempty-only increment around ST; kind `exit` iff `exitStageName is not null` |

**Honest residual (not stop fail):** execute-time `DomainExpressionLoweringPass` remains for parameter bindings / create prevalidate / count-filter helpers (`DomainEntityInstance.cs:665`, HostAbi `:713`, `:775`). Plan stop named **LowerActionBody** + policy re-lower + Domain-null standalone effect/policy — closed on tip.

## F1 re-check (Razor)

| Question | Finding |
|---|---|
| Comment at HostAbi `:330` | Still present: `// Domain-null standalone: keep execute-time lower path.` |
| Actual Domain-null ExecuteEffectList | Throws at `DomainEntityInstance.cs:775-777` — no `LowerActionBody` |
| Domain-null subscription arm `:326-337` | Builds `EffectLoweringPass`, `BindPeerInEffect`, then `ExecuteEffectList` → always throws |
| Honesty bug or fail-open? | **Honesty bug** (invariant comment lies per protocol §3.9). **Not fail-open** — fail-closed throw still fires. Pure-ST Domain-null nested transitions that never call ExecuteEffectList are a separate sibling (out of F1 severity upgrade) |
| Severity | Keep **bug** (lying invariant comment). Prefer early Domain-null throw before constructing EffectLoweringPass (same as EvaluatePolicy) |

## Spot-check F2–F5

| ID | Razor severity | Final Boss | Notes |
|---|---|---|---|
| F2 | suggestion | **OPEN suggestion** | Domain-null arm still dead dual-path (`:326-337`); no fail-open |
| F3 | suggestion | **OPEN suggestion** | `effectPass` param on `ExecuteEffectList` `:693` — body never reads it (only signature). `TransitionStage` still constructs `exitPass`/`entryPass` `:163-178` solely to thread unused arg |
| F4 | suggestion | **OPEN suggestion** | Suite gaps: Clear(seg1), Domain-null TransitionStage with non-ST entry effects, optional ReferenceEquals. Clear(seg0)+Clear(policy) remain hard primary oracles |
| F5 | nit | **OPEN nit** | `CacheEntryExitSegments` always runs `:241-243` / `:257-259` even with no ST → unused seg0 twin of EntryExitBodies. Harmless; execute no-nested ignores segments |

No upgrade: none of F2–F5 demonstrated fail-open on tip.

## Non-regression: PR74 F1 UseThis:false

| Item | Tip `0c5b58ee` |
|---|---|
| HostAbi SubscriptionBodies UseThis:false comment | **CLOSED / still honest** at `:315-317` |
| Cache producer | `BuildSubscriptionCaches` `UseThisReference: false` at `RuntimeAnalysisCache.cs:413` |

## Issues

### Issue 1 -- Severity: bug
- File: Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:330
- Description: False invariant comment after Slice B. Says Domain-null standalone keeps execute-time lower path; `ExecuteEffectList` Domain-null now always throws (`DomainEntityInstance.cs:775-777`) with no `LowerActionBody`. Domain-null subscription arm (`:326-337`) constructs EffectLoweringPass + BindPeerInEffect then hits that throw. Protocol §3.9: invariant comments are checklists — this advertises a dual-path Slice B removed. Same honesty class as PR74 F1. Not a demonstrated fail-open.
- Suggestion: Replace with fail-closed wording (Domain-null subscription / effect lists require Domain-bound module). Prefer early throw with Domain-null message before constructing EffectLoweringPass (mirror EvaluatePolicy `:383-385`).
- Status: open

### Issue 2 -- Severity: suggestion
- File: Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:326-337
- Description: Dead dual-path residue: Domain-null arm still allocates EffectLoweringPass and peer-binds Ontology effects before the inevitable ExecuteEffectList throw. Sibling Domain-bound arm correctly binds cache only.
- Suggestion: Fail closed immediately when `Domain is null`; delete BindPeerInEffect execute-time lower setup.
- Status: open

### Issue 3 -- Severity: suggestion
- File: Poly/DomainModeling/Runtime/DomainEntityInstance.cs:693 + HostAbi.cs:163-178
- Description: `ExecuteEffectList` still takes `EffectLoweringPass effectPass` but never reads it. `TransitionStage` still constructs exitPass/entryPass solely to thread the unused argument. Not execute-time lowering, but weakens “execute never lowers” readability and leaves a footgun if someone reintroduces `effectPass.LowerActionBody`.
- Suggestion: Drop unused parameter from ExecuteEffectList / RunTransitionEffectList; stop constructing EffectLoweringPass on Domain-bound transition paths.
- Status: open

### Issue 4 -- Severity: suggestion
- File: Poly.Tests/DomainModeling/Compile/SliceBLowerAtLowerTests.cs (suite gap)
- Description: Hard oracles cover Clear(seg0), Clear(policy), Domain-null EvaluatePolicy, happy nested Mid. Gaps: Clear(seg1)/multi-ST; Domain-null TransitionStage with non-ST entry effects → Domain-bound-module throw; optional ReferenceEquals segment body identity.
- Suggestion: Add SliceB-named tests for those siblings; keep Clear as primary no-relower proof.
- Status: open

### Issue 5 -- Severity: nit
- File: Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs:241-243 (and exit twin)
- Description: CacheEntryExitSegments always runs, including lists with no StageTransitionEffect, producing unused segment 0 twin of EntryExitBodies. Execute’s no-nested path ignores segments. Harmless cache waste.
- Suggestion: Skip when `effects.All(e => e is not StageTransitionEffect)`, or share Body with EntryExitBodies for a single segment.
- Status: open

## Prior follow-ups disposition (Razor PR75 + PR74)

| ID | Disposition |
|---|---|
| Razor F1 HostAbi Domain-null lower comment | **OPEN** — confirmed honesty bug; not fail-open |
| Razor F2–F5 | **OPEN** — severities unchanged |
| PR74 F1 HostAbi UseThis:false | **CLOSED** (still) `:315-317` |
| PR74 F2–F5 | **still open** (unchanged; out of Slice B stop) |

## Checklist (protocol §9)

- [x] Diff collected vs `origin/master` (+306/−70 / 5 files); tip SHA matched
- [x] Stance adversarial; Razor treated as hypotheses; primary greps/reads this process
- [x] Stop table proved MET with path:line
- [x] Sibling-path: Domain-null vs Domain-bound; pure-ST vs mixed; EntryExitBodies vs segments
- [x] F1 reachability: Domain-null throw reachable; comment lying; not fail-open
- [x] Invariant comments checked (PR74 F1 closed; new F1 at `:330`)
- [x] Oracles not weakened; Clear* prove no re-lower
- [x] Counts recomputed (allowExecuteTimeLower=0; Runtime LowerActionBody=0; cache sites=2)
- [x] Review + follow-ups written under docs/agent/reviews/ (Final Boss only; Razor files not overwritten)
- [x] No product/runtime code; no merge
