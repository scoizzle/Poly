# PR 51 — Razor follow-ups — 2026-09-06 (`574e941a`)

- **SHA:** `574e941ab80a0e2ff9a2429ea9f411cf943a16bb`
- **Verdict:** ship — F1 closed

## Prior F1

- [x] **F1** — `DomainEntityInstance.cs:803-809` + `CrmDogfoodTests.cs:210-219` — FIXED (fail-closed Failure + Billing.Charge oracle)

## Still open (suggestions — not ship blockers)

- [ ] **F2** — `RuntimeAnalysisCache.cs:103-119` vacuous OnEntry module preference
- [ ] **F3** — `DomainEntityInstance.cs:693-733` per-invoke module re-analysis
- [ ] **F4** — `DomainEntityInstance.cs:780+` Variable rewrite vs shadowing locals
- [ ] **F5** — `DomainEntityInstance.cs:726` empty-body stubs can mask missing rewrites
- [ ] **F6** — `DslCompiler.cs` `RequireHttpActionsInModule` broader than HTTP

## Nits

- [ ] **N1** — dead `args` on `ExecuteEffectList`
- [ ] **N2** — stale `DomainSession.Lower` doc

## Freeze

Filed for Final Boss. Never implement from this review.
