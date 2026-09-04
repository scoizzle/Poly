# PR 51 final-pass follow-ups — 2026-09-04

- [x] **F1** — Prove named invoke runs the cached tree: replace the `Lot.Issue` operation with a no-op Success and assert zero children (`InvokeAction_RunsTheCachedTree_NotAReloweredEffectWalk`).
- [x] **F2** — Rewrite `docs/interpretation/domain-execution-model.md` §2 / §2c / §9 so it matches lookup, pair-shaped Store jobs, and BCL clocks.

Open residuals (not this PR):

- [ ] **F3** — Cache `EvaluatePolicy` guard trees the same way as named actions (`RuntimeAnalysisCache` + `DomainEntityInstance.EvaluatePolicy`).
- [ ] **F4** — Subscription / transition-batch bodies: populate at `GetOrLower` or keep execute-time `LowerActionBody` with an explicit key.
- [ ] **F5** — Unbound `RuntimeAnalysisCache` fallback still `ForExtensions(core ids)` — vendor maps dropped until `Analyze` binds.
