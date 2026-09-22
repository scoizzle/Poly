# PR 75 Slice B — Final Boss follow-ups — 2026-09-22

Target tip: `74e448864dc4691470664312ac3c9652ebfe4329`.
Prior ship: `3487f44efc80c5e2f92de3941835c7c4302e657d`.
Review: `docs/agent/reviews/2026-09-22-pr75-74e44886-final-boss.md`.
Mode: re-verify of F2–F5 fix-up (skip Razor).
Model: grok-4.6 / executor hands OK.

**Verdict**: **ship**. F1–F5 **CLOSED**. Stop still **MET**. Open F#: none.

## Disposition table (F1–F5)

| ID | Severity | Prior (3487f44e) | Tip 74e44886 |
|---|---|---|---|
| F1 | bug | CLOSED — HostAbi Domain-null comment honesty | **CLOSED** — tip strengthens: Domain-null subscription throws immediately HostAbi `:286-289`; ExecuteEffectList `:756-758`; EvaluatePolicy `:375-377` |
| F2 | suggestion | OPEN — Domain-null sub dead dual-path | **CLOSED** — HostAbi `:286-289` immediate throw; no EffectLoweringPass / BindPeerInEffect / ExecuteEffectList after Domain-null |
| F3 | suggestion | OPEN — unused effectPass | **CLOSED** — ExecuteEffectList `:673+` / RunTransitionEffectList `:199+` drop EffectLoweringPass; TransitionStage no longer constructs passes; Runtime EffectLoweringPass=0 |
| F4 | suggestion | OPEN — SliceB suite gaps | **CLOSED** — `SliceB_ClearEntryExitSegment1_ThrowsFailClosed_NoRelower` `:154`; `SliceB_TransitionStage_DomainNull_NonStEntry_ThrowsFailClosed` `:187`; `SliceB_EntryExitSegmentBody_MatchesGetOrLowerIdentity` `:210` |
| F5 | nit | OPEN — CacheEntryExitSegments always runs | **CLOSED** — RuntimeAnalysisCache `:284-286` early return when no StageTransitionEffect |

No severity upgrade. No fail-open found. Open F#: **none**.

## Open

_(none — F1–F5 CLOSED)_

## Carry (PR74 — still open, not Slice B ship blockers)

- [ ] **PR74-F2** — `RewriteVoidFailClosedThrow` soft `_ => node` (`DomainEntityInstance.cs` ~864+)
- [ ] **PR74-F3** — entry/exit `TryGetExitMethod` / `TryGetEntryMethod` fallback skips `RewriteVoidFailClosedThrow`
- [ ] **PR74-F4** — OnEntry/OnExit `existingNames` collision → EntryExitBodies fresh Body vs module old method
- [ ] **PR74-F5** — SliceA `ClearEntryExitBody` miss-throw oracle still missing (Slice B Clear**Segment** is separate)

## Closed this tip / prior

- [x] **F1** — HostAbi Domain-null execute-time lower comment honesty — CLOSED at `3487f44e`; tip `74e44886` Domain-null subscription immediate throw `:286-289`
- [x] **F2** — Domain-null subscription fail-closed immediately — **CLOSED** HostAbi `:286-289`
- [x] **F3** — unused EffectLoweringPass dropped from ExecuteEffectList / RunTransitionEffectList — **CLOSED**
- [x] **F4** — SliceB oracles (Clear seg1, Domain-null ST/non-ST entry, optional identity) — **CLOSED**
- [x] **F5** — CacheEntryExitSegments skip when no StageTransitionEffect — **CLOSED** `:284-286`
- [x] **PR74-F1** — HostAbi SubscriptionBodies UseThis:false comment — still CLOSED / honest at `:304-306`
- [x] **Stop condition** — execute never LowerActionBody on claimed Runtime hot paths; Interpreter binds GetOrLower bodies; Domain-null ExecuteEffectList/EvaluatePolicy/subscription fail closed; SliceB Clear* hard oracles (recomputed at tip)

## Out of scope (do not reopen as Slice B ship blockers)

- DEI delete, Item 5 Occupancy, CURRENT, producer-loop C, DEI-not-sim E, PR 73
- Plans docs / PIPELINE-STATUS admission
- Residual execute-time `DomainExpressionLoweringPass` for bindings / create prevalidate / count filters (outside LowerActionBody stop)
- Policy + subscription Parameter twins (explicit Slice A residuals)
- Razor re-run (path: Final Boss re-verify skip Razor)
- Dead unreachable `BindPeerInEffect` private helper (HostAbi `:473+`) — not fail-open; optional cleanup outside this re-verify
