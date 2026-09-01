# Follow-ups — PR 44 unique-before-mutate (round 3) — Final Boss — 2026-08-31

- Source review: `docs/agent/reviews/2026-08-31-pr44-740b5c87-final-boss.md`
- Target: PR 44 SHA 740b5c87 vs origin/master
- Model: `opencode-go/mimo-v2.5`; executor re-checked current source + code-path tracing

## Open bugs (must close before ship)

None. F1 and F2 are closed at this SHA.

## Suggestions

- [ ] **F3 — CreateChildInstance validates a partial bag; unique-from-default missed** — File: `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:452`, `:486` — Do: fill missing scalar props with `EvaluateDefaultValue` before `ValidateConstraints(targetEntity, initialValues, Store)`, mirroring `PrevalidateCreateInitializers` (`:386–417`).

- [ ] **F5 — no prevalidate on entry/exit/subscription effect sequences** — File: `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:149–155`, `:189–219`; `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:608–615` — Do: route `TransitionStage` entry/exit and `ExecuteSubscriptionEffects` lists through `PrevalidateUnconditionalCreates` (or fail the transition/subscription on a non-null prevalidate error), and propagate `ExecuteEffect` failures (call `RestoreActionState` or equivalent). Today `ExecuteEffect` converts a colliding create to `DomainResult.Failure` and both callers discard it, so the sequence completes with prior effects committed and the failure invisible.

## Nits

- [ ] **F6 — Duplicate Unique loop** — File: `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:183–189` — Do: collapse per-prop `UniqueConstraint` into a single `UniqueCollisionMessage` call over the full bag (currently O(n²) and can return another prop's message).

## Disposition of prior items (re-verified this SHA from current source)

- **F1 (round-2 / 84fc3c1c)**: VM-lowered conditional assigns bypass unique-before-mutate — **fixed** at this SHA via `ContainsUniqueAssign` (`DomainEntityInstance.cs:714–723`) routing unique-containing conditionals to structured path. Three tests added (`:570` if-assign, `:598` else-assign, `:628` nested-if-assign).
- **F2 (round-2 / 84fc3c1c)**: effect-dependent conditional create prevalidate drift — **fixed** at this SHA via `ConditionIsEffectIndependent` (`HostAbi.cs:356–370`) skipping prevalidation for condition-dependent conditionals, and `RestoreActionState` (`DomainEntityInstance.cs:725–736`) rolling back prior assigns on failure. Two tests added (`:660` drift, `:698` inverse).
- **F3 (round-1 / 9c739546)**: CreateChildInstance partial-bag unique — **still open** (`HostAbi.cs:486`).
- **F4 (round-1 / 9c739546)**: missing sibling tests — **addressed** (5 tests added at this SHA: `:570`, `:598`, `:628`, `:660`, `:698`).
- **F5 (round-1 / 9c739546)**: no prevalidate on entry/exit/subscription — **still open** (`HostAbi.cs:149–155`, `:189–219`).
- **F6 (round-1 / 9c739546)**: O(n²) unique loop — **still open** (`DomainEntityInstance.cs:183–189`).
