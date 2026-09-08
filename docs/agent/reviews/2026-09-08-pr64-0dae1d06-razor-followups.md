# Follow-ups — PR 64 Item 4 restore — Razor — 2026-09-08

- Source review: `docs/agent/reviews/2026-09-08-pr64-0dae1d06-razor.md`
- Target: PR 64 SHA `0dae1d06d0127c0a030c60ee3697e50520cee9fa`
- Reviewer: Razor

## Open bugs (must close before ship)

None.

## Suggestions

- [ ] **F1 — MissingReturn does not RestoreActionState** — File: `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:617–618` — Do: before `ActionInvocationResult.MissingReturn`, call `RestoreActionState(bagBefore, stageBefore, createdBefore)` (same snapshots as the ExecuteEffectList Failure arm), **or** document + prove unreachability on domain-bound analyzed invoke (DMEFF010). Add a test that reaches MissingReturn (or an explicit unreachable-on-analyzed-domains note in the review disposition).

- [ ] **F2 — Notify / subscription side effects survive RestoreActionState** — File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:426–432`; `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:21–23`; `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:1032–1042` — Do: decide whether action-level Failure must be transactional w.r.t. `NotifyTransition` (defer Notify until Success, or revert subscriber mutates). Add a test: transition notifies a subscriber that assigns, then a later effect in the same invoke returns Failure → subscriber bag unchanged and stage restored.

## Nits

- [ ] **F3 — Strengthen nested-require restore oracle** — File: `Poly.Tests/DomainModeling/ActionEntityReturnTests.cs:1216–1239` — Do: assert `ErrorMessage` / `FailedGuards` names AlwaysFail (or blocked-by-policy text) in addition to `Flag==0`.

## Disposition of prior related items

- **PR 43 IfOnMutatedProperty documented miss** (prior assigns kept on non-Unique Failure): **fixed** at this SHA — Unique-only gate removed (`DomainEntityInstance.cs:598–603`); test retargeted to `DoesNotApplyPriorAssign` (`ActionEntityReturnTests.cs:1183`).
- **PR 44 F2 Unique-only RestoreActionState**: **superseded / widened** — restore now on any ExecuteEffectList Failure, not Unique substring.
- **PR 44 F5 entry/exit/subscription discard Failure**: **still open** (not this PR); see `2026-08-31-pr44-740b5c87-final-boss-followups.md`. Related to F2 here only insofar as subscription Notify fan-out remains non-transactional.
- **PR 62 public Create wires inverses**: **untouched** — tip diff does not include exporter Notify/StoreBind/CORE paths from PR 62.
