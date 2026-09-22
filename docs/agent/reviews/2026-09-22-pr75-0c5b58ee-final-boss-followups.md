# PR 75 Slice B — Final Boss follow-ups — 2026-09-22

Target tip: `0c5b58eebfd102144828a6c9c0d785e5090a3a3b`.
Review: `docs/agent/reviews/2026-09-22-pr75-0c5b58ee-final-boss.md`.
Mode: re-verify of Razor `docs/agent/reviews/2026-09-22-pr75-0c5b58ee-razor.md` (+ followups).
Model: grok-4.6 / executor hands OK.

**Verdict**: **ship**. Stop **MET**. Open: F1–F5 (F1 honesty bug preferred close; not stop fail).

## Razor disposition table

| Razor ID | Severity | Final Boss | Disposition |
|---|---|---|---|
| F1 | bug | bug | **OPEN** — HostAbi `:330` still lies; ExecuteEffectList Domain-null throws (`:775-777`); honesty not fail-open |
| F2 | suggestion | suggestion | **OPEN** — Domain-null subscription dead dual-path `:326-337` |
| F3 | suggestion | suggestion | **OPEN** — unused `effectPass` on ExecuteEffectList `:693`; TransitionStage constructs passes `:163-178` |
| F4 | suggestion | suggestion | **OPEN** — SliceB suite gaps (Clear seg1 / Domain-null mixed TransitionStage / optional ReferenceEquals) |
| F5 | nit | nit | **OPEN** — CacheEntryExitSegments always runs `:241-243` |

No Razor finding invalidated. No severity upgrade (no fail-open).

## Open

- [ ] **F1** — **bug** — `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:330` — Fix false invariant comment `Domain-null standalone: keep execute-time lower path.` `ExecuteEffectList` Domain-null throws (`DomainEntityInstance.cs:775-777`); no execute-time `LowerActionBody`. Align comment with fail-closed Domain-bound-module requirement; prefer early throw before constructing `EffectLoweringPass`. Prefer before merge (honesty; not stop fail).

- [ ] **F2** — **suggestion** — `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:326-337` — Domain-null subscription arm: delete dead `EffectLoweringPass` + `BindPeerInEffect` + `ExecuteEffectList` setup; fail closed immediately when `Domain is null` (mirror EvaluatePolicy).

- [ ] **F3** — **suggestion** — `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:693` + HostAbi `TransitionStage` `:163-178` — Remove unused `EffectLoweringPass effectPass` parameter from `ExecuteEffectList` / `RunTransitionEffectList`; stop constructing passes solely to thread a dead arg.

- [ ] **F4** — **suggestion** — `Poly.Tests/DomainModeling/Compile/SliceBLowerAtLowerTests.cs` — Add SliceB oracles: Clear(seg1) / multi-ST; Domain-null TransitionStage with non-ST entry effects → Domain-bound-module throw; optional ReferenceEquals segment body identity.

- [ ] **F5** — **nit** — `Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs:241-243` — Skip or share `CacheEntryExitSegments` when the list has no `StageTransitionEffect` (avoid unused seg0 twin of EntryExitBodies).

## Carry (PR74 — still open, not Slice B ship blockers)

- [ ] **PR74-F2** — `RewriteVoidFailClosedThrow` soft `_ => node` (`DomainEntityInstance.cs` ~864+)
- [ ] **PR74-F3** — entry/exit `TryGetExitMethod` / `TryGetEntryMethod` fallback skips `RewriteVoidFailClosedThrow` (`:754-761`)
- [ ] **PR74-F4** — OnEntry/OnExit `existingNames` collision → EntryExitBodies fresh Body vs module old method
- [ ] **PR74-F5** — SliceA `ClearEntryExitBody` miss-throw oracle still missing (Slice B Clear**Segment** is separate)

## Closed this tip / prior

- [x] **PR74-F1** — HostAbi SubscriptionBodies UseThis:false comment — still CLOSED / honest at `:315-317` (cache `UseThisReference: false` at `RuntimeAnalysisCache.cs:413`)
- [x] **Stop condition** — execute never LowerActionBody on claimed Runtime hot paths; Interpreter binds GetOrLower bodies; Domain-null ExecuteEffectList/EvaluatePolicy fail closed; SliceB Clear* hard oracles

## Out of scope (do not reopen as Slice B ship blockers)

- DEI delete, Item 5 Occupancy, CURRENT, producer-loop C, DEI-not-sim E, PR 73
- Plans docs / PIPELINE-STATUS admission
- Residual execute-time `DomainExpressionLoweringPass` for bindings / create prevalidate / count filters (outside LowerActionBody stop)
- Policy + subscription Parameter twins (explicit Slice A residuals)
