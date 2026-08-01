# DomainModeling quality delta — 2026-08-01

- **Target**: local uncommitted changes
- **Mode**: multi (Pass A = session; Pass B = independent read-only subagent, diff-only)
- **Issue counts**: 0 bugs, 6 suggestions, 1 nit
- **Verdict**: Ship-ready as a fail-closed hardening slice; no blocking product bugs on valid full-pipeline domains. Residual is incomplete test oracles and soft OnExit/OnEntry / subscription lowering siblings.
- **Diff stats**: 10 files, +168 / −54

## Summary

This delta tightens domain-bound runtime around `DomainCatalogMetadata`: catalog pass structural-fails when Semantic DTLM/RLM are absent; `AnalyzeRequiringCatalog` + `RuntimeAnalysisCache.GetOrAnalyze` require catalog on non-failed trees; `TransitionStage` / `NotifyTransition` re-check catalog; transition re-entrancy is depth-capped; OnExit/OnEntry use analysis-aware `LoweringContext`. Happy-path and stripped-catalog runtime throws look real. Dominant residual risks: tests that name the wrong SUT for notify catalog throw, untested CatalogPass / `AnalyzeRequiringCatalog` throw branches, domain-bound OnExit/OnEntry still soft-skip on `TryGetStage` miss, subscription effect lowering still analysis-blind, and re-entrancy depth only proven standalone.

## Issues

### Issue 1 -- Severity: suggestion

- File: `Poly.Tests/DomainModeling/Analysis/DomainInstanceStoreFailClosedTests.cs:91`
- Description: `NotifyTransition_Throws_WhenDomainCatalogMissing` strips catalog then calls `TransitionStage`. Domain-bound `TransitionStage` throws at its catalog check **before** `Store.NotifyTransition`, so the store’s catalog throw is never exercised. Assertion only matches shared `"DomainCatalogMetadata"` text.
- Suggestion: Call `store.NotifyTransition` directly after strip (or split tests / distinct message substrings for transition vs dispatch).
- Status: closed — split oracles: `TransitionStage_Throws_WhenDomainCatalogMissing` vs direct `store.NotifyTransition` after `notifyStore: false`; distinct message substrings.

### Issue 2 -- Severity: suggestion

- File: `Poly/DomainModeling/Analysis/DomainCatalogPass.cs:23`
- Description: Fail-closed structural failure when DTLM/RLM missing has no withhold/oracle test. Reachable mainly on partial pipelines, not valid full-analyze domains.
- Suggestion: Pipeline or unit host that runs catalog without Semantic bags; assert `HasStructuralFailure`, diagnostic text, absent catalog; assert `AnalyzeRequiringCatalog` returns without throw when structural failure is set.
- Status: closed — `DomainCatalogPassFailClosedTests` invokes CatalogPass on bare `AnalysisContext` (Builder refuses dep-less registration); RequireCatalog no-throw on structural failure.

### Issue 3 -- Severity: suggestion

- File: `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs:35`
- Description: `AnalyzeRequiringCatalog` throw branch (success tree, no catalog) is untested; only happy path covered.
- Suggestion: Synthetic analysis or pipeline without `DomainCatalogPass`; assert exact `InvalidOperationException` message.
- Status: closed — `RequireCatalog` extracted; success-tree strip of `DomainCatalogMetadata` asserts throw; structural-failure path returns without throw.

### Issue 4 -- Severity: suggestion

- File: `Poly/DomainModeling/DomainEntityInstance.cs:620`
- Description: Domain-bound transitions hard-require catalog but still soft-skip OnExit/OnEntry when `TryGetStage` fails (stage still advances). Weaker than `InvokeActionInternal` ESM fail-closed; intentional soft-skip comment was removed without replacing characterization.
- Suggestion: Fail loud on stage resolve miss when domain-bound, or document + test silent effect loss when ESM stripped.
- Status: closed — `ResolveTransitionStage` throws on ESM miss when analysis present; `TransitionStage_DomainBound_Throws_WhenEntityStructureMetadataMissing`.

### Issue 5 -- Severity: suggestion

- File: `Poly/DomainModeling/DomainEntityInstance.cs:710`
- Description: OnExit/OnEntry now analysis-aware; `ExecuteSubscriptionEffects` still builds `EffectLoweringPass` without analysis/domain. Dual lowering paths for domain-bound runtime.
- Suggestion: Pass `LoweringContext` from `GetOrAnalyze` when `Domain` non-null; test domain-dependent subscription effect path.
- Status: closed — domain-bound path uses `LoweringContext(Analysis, Domain)`; regression `ExecuteSubscriptionEffects_DomainBound_UsesAnalysisAwareLowering`.

### Issue 6 -- Severity: suggestion

- File: `Poly.Tests/DomainModeling/DomainEntityInstanceTests.cs:222`
- Description: Re-entrancy depth only proven standalone (`Domain` null). Domain-bound sibling (catalog + TryGetStage + analysis-aware lowering) untested for depth exhaustion.
- Suggestion: Domain-bound chain test asserting same throw; optionally depth restore after throw.
- Status: closed — `TransitionStage_Reentrancy_ExceedsMaxDepth_Throws_DomainBound` (+ standalone sibling renamed).

### Issue 7 -- Severity: nit

- File: `Poly/DomainModeling/DomainEntityInstance.cs:584`
- Description: Remarks still say OnEntry re-entrancy should be avoided until a depth budget is added; code now enforces `MaxTransitionDepth`.
- Suggestion: Update remarks to describe the budget and partial stage application on nested throw.
- Status: closed — remarks describe `MaxTransitionDepth` and store cascade limit.

## Verified-correct notes

- Catalog strip → `InvokeAction` / `TransitionStage` throw with catalog message (domain-bound).
- Standalone re-entrancy depth throw green.
- `AnalyzeRequiringCatalog` happy path publishes catalog + action map.
- `GetEffectiveActions` consolidation is behavior-preserving (Pass B).
- Transition depth counter restored in `finally` on throw.
- No `DM-META-REMOVE-FALLBACK` reintroduced in this delta.

## Checklist

- [x] Diff collected; scope drift noted (quality-only 10 files)
- [x] Adversarial / multi Pass B
- [x] Sibling-path considered (standalone vs domain-bound; TransitionStage vs NotifyTransition)
- [x] Reachability on new throws considered
- [x] Primary evidence from current tree + Pass B
- [x] Review under `docs/agent/reviews/`
- [x] Follow-ups under `docs/plans/simple-agent-tasks/`
