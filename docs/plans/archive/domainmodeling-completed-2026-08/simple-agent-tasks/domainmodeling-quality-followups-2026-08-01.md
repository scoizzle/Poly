# DomainModeling quality follow-ups — 2026-08-01

**Source review:** [`../../agent/reviews/2026-08-01-domainmodeling-quality-delta.md`](../../agent/reviews/2026-08-01-domainmodeling-quality-delta.md)  
**Parent context:** DAS suite complete; this is a post-DAS hardening delta  
**Status:** `[x]` Closed 2026-08-01  

## Pick order

1. Test oracle honesty (Q1–Q3, Q6)  
2. Soft OnExit/OnEntry / subscription lowering (Q4–Q5)  
3. Doc nit (Q7)

## Tasks

- [x] **Q1** — Exercise `NotifyTransition` catalog fail-closed directly  
  Split: `TransitionStage_Throws_WhenDomainCatalogMissing` (message contains `TransitionStage`) vs
  `NotifyTransition_Throws_WhenDomainCatalogMissing` calling `store.NotifyTransition` after
  `TransitionStage(..., notifyStore: false)` (message contains `NotifyTransition`).

- [x] **Q2** — Oracle for `DomainCatalogPass` structural failure without DTLM/RLM  
  `DomainCatalogPassFailClosedTests`: HasStructuralFailure + diagnostic + no catalog;
  `RequireCatalog` no-throw on structural failure.

- [x] **Q3** — Oracle for catalog-required throw without structural failure  
  `RequireCatalog_Throws_WhenCatalogMissingWithoutStructuralFailure` (Semantic-only pipeline);
  `RequireCatalog` extracted from `AnalyzeRequiringCatalog`.

- [x] **Q4** — Domain-bound OnExit/OnEntry stage miss fail-loud  
  `ResolveTransitionStage` throws when analysis present and `TryGetStage` fails;
  test `TransitionStage_DomainBound_Throws_WhenEntityStructureMetadataMissing`.

- [x] **Q5** — Analysis-aware subscription effect lowering  
  `ExecuteSubscriptionEffects` uses `LoweringContext` with analysis/domain when domain-bound;
  regression `ExecuteSubscriptionEffects_DomainBound_UsesAnalysisAwareLowering`.

- [x] **Q6** — Domain-bound `MaxTransitionDepth` test  
  `TransitionStage_Reentrancy_ExceedsMaxDepth_Throws_DomainBound` (+ standalone sibling renamed).

- [x] **Q7** — Fix stale TransitionStage remarks about missing depth budget  
  Remarks describe `MaxTransitionDepth` and store cascade limit.

## Done definition

1. [x] Q1–Q3 green (oracles match real SUTs).  
2. [x] Q4–Q5 fixed with tests.  
3. [x] Q6–Q7 closed.  
4. [x] No new dual semantic path without docs.
