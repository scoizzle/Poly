# Follow-ups — PR 51 pipeline (Final Boss re-verify) — 2026-09-06

- Source review: `docs/agent/reviews/2026-09-06-pr51-8b995997-final-boss.md`
- Target: PR 51 SHA `8b9959977be1c8e5c439452d83c65a29e9660f92` vs `origin/master`
- Mode: re-verify of Razor `docs/agent/reviews/2026-09-06-pr51-8b995997-razor.md` / `docs/agent/reviews/2026-09-06-pr51-8b995997-razor-followups.md`
- Model: grok-4.6
- Verdict: ship

## Open bugs (must close before ship)

None.

## Suggestions

- [ ] **F8** — `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:807-811`. Do: keep `Success(BindThis(arg))` now that CLR `object` accepts modeled AST types (`TypeDefinitionExtensions.cs:119-123`), **or** document in CORE / `docs/interpretation/domain-execution-model.md` that simulate typed returns are CreatedChildren-only (`DomainEntityInstance.cs:574-590`) and ignore Success payload (`:680-682`). Update the stale comment at `:803-805` (it still says TypeDefs are not assignable to object after `55b5a588`). Optional lock: a test that would fail if Create/CreateIn existed only as the Success argument (side-effect must remain an assignment — `EffectLoweringPass.cs:901-917`).

## Nits

None.

## Process

GitHub mergeable is CONFLICTING against current `origin/master` (later PRs, including PR 56 Type-create emit). Final Boss re-verified **this tip** only. Do not merge. Do not resolve conflicts from this review.

`--treenode-filter` on this host did not isolate named tests (TUnit: at most 1 argument; class filters still ran the full 2785). Full-suite green is evidence; it is not a substitute for a working class filter next time.

## Disposition of prior items (Razor @ 8b995997, re-read this SHA)

- **F1** — adapter fail-closed — **still closed**. `DomainEntityInstance.cs:819-825` `Return(DomainResult.Failure(...))`; Capture `CrmDogfoodTests.cs:217-219` `Succeeded == false` + `Billing.Charge`; Shop Pay `PipelineTransformationTests.cs:173-203`. Pattern order keeps adapter after Success/Failure arity arms.
- **BindThis DomainResult Success/Failure arity (`edd1b8a9`)** — **OK**. Success → `Success()` (`:807-811`); Failure keeps string args (`:812-818`). Create not dropped (assignment before wrap).
- **F8** — Success value discard vs CreatedChildren / export Success(value) — **still open** (suggestion). Independently confirmed; not a ship blocker (Foreman / Scot).
- **Pipeline (simulate create/create-in, `session.Lower`, one tree)** — **holds**. `rg LowerStageTransitions` / `PreprocessRuntimeKeyword` empty in `*.cs`. Named invoke uses `GetOrLower` + BindThis. `CreateCore` `:227` auto-links Fine Type (same helper as `CreateChildInstance` `:640`).
- **Razor-carried F2–F6** (OnEntry preference, per-invoke re-analysis, Variable rewrite, empty stubs, RequireHttpActionsInModule) — **not re-opened** as ship blockers; not fully re-traced this pass.

## Freeze

Filed for ship. Never implement from this review. Never merge. Never force-push product.
