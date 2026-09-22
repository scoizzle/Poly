# PR 75 Slice B — Final Boss follow-ups — 2026-09-22

Target tip: `3487f44efc80c5e2f92de3941835c7c4302e657d`.
Prior ship: `0c5b58eebfd102144828a6c9c0d785e5090a3a3b`.
Review: `docs/agent/reviews/2026-09-22-pr75-3487f44e-final-boss.md`.
Mode: re-verify of F1 fix-up only (skip Razor).
Model: grok-4.6 / executor hands OK.

**Verdict**: **ship**. F1 **CLOSED**. Stop still **MET**. Open: F2–F5 (suggestion/nit; no fail-open).

## Disposition table (vs prior Final Boss ship)

| ID | Severity | Prior (0c5b58ee) | Tip 3487f44e |
|---|---|---|---|
| F1 | bug | OPEN — lying Domain-null lower comment | **CLOSED** — HostAbi `:330-331` fail-closed / Domain-bound module / no execute-time LowerActionBody; ExecuteEffectList `:775-777` still throws |
| F2 | suggestion | OPEN | **OPEN** — Domain-null subscription dead dual-path `:326-338` (comment now names F2) |
| F3 | suggestion | OPEN | **OPEN** — unused `effectPass` on ExecuteEffectList `:693` |
| F4 | suggestion | OPEN | **OPEN** — SliceB suite gaps |
| F5 | nit | OPEN | **OPEN** — CacheEntryExitSegments always runs |

No severity upgrade. No fail-open found.

## Open

- [ ] **F2** — **suggestion** — `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:326-338` — Domain-null subscription arm: delete dead `EffectLoweringPass` + `BindPeerInEffect` + `ExecuteEffectList` setup; fail closed immediately when `Domain is null` (mirror EvaluatePolicy). Tip comment already admits dead setup.

- [ ] **F3** — **suggestion** — `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:693` + HostAbi `TransitionStage` — Remove unused `EffectLoweringPass effectPass` parameter from `ExecuteEffectList` / `RunTransitionEffectList`; stop constructing passes solely to thread a dead arg.

- [ ] **F4** — **suggestion** — `Poly.Tests/DomainModeling/Compile/SliceBLowerAtLowerTests.cs` — Add SliceB oracles: Clear(seg1) / multi-ST; Domain-null TransitionStage with non-ST entry effects → Domain-bound-module throw; optional ReferenceEquals segment body identity.

- [ ] **F5** — **nit** — `Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs:241-243` — Skip or share `CacheEntryExitSegments` when the list has no `StageTransitionEffect` (avoid unused seg0 twin of EntryExitBodies).

## Carry (PR74 — still open, not Slice B ship blockers)

- [ ] **PR74-F2** — `RewriteVoidFailClosedThrow` soft `_ => node` (`DomainEntityInstance.cs` ~864+)
- [ ] **PR74-F3** — entry/exit `TryGetExitMethod` / `TryGetEntryMethod` fallback skips `RewriteVoidFailClosedThrow`
- [ ] **PR74-F4** — OnEntry/OnExit `existingNames` collision → EntryExitBodies fresh Body vs module old method
- [ ] **PR74-F5** — SliceA `ClearEntryExitBody` miss-throw oracle still missing (Slice B Clear**Segment** is separate)

## Closed this tip / prior

- [x] **F1** — HostAbi Domain-null execute-time lower comment honesty — **CLOSED** at tip `3487f44e` `:330-331` (was lying at `0c5b58ee`; ExecuteEffectList Domain-null throw unchanged `:775-777`)
- [x] **PR74-F1** — HostAbi SubscriptionBodies UseThis:false comment — still CLOSED / honest at `:315-317`
- [x] **Stop condition** — execute never LowerActionBody on claimed Runtime hot paths; Interpreter binds GetOrLower bodies; Domain-null ExecuteEffectList/EvaluatePolicy fail closed; SliceB Clear* hard oracles (recomputed at tip)

## Out of scope (do not reopen as Slice B ship blockers)

- DEI delete, Item 5 Occupancy, CURRENT, producer-loop C, DEI-not-sim E, PR 73
- Plans docs / PIPELINE-STATUS admission
- Residual execute-time `DomainExpressionLoweringPass` for bindings / create prevalidate / count filters (outside LowerActionBody stop)
- Policy + subscription Parameter twins (explicit Slice A residuals)
- Razor re-run (path: Final Boss re-verify skip Razor)
