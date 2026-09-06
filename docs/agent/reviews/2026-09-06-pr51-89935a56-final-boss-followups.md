# Follow-ups — PR 51 pipeline (Final Boss re-verify) — 2026-09-06

- Source review: `docs/agent/reviews/2026-09-06-pr51-89935a56-final-boss.md`
- Target: PR 51 SHA `89935a56384de46a8504f6d6518563ac85b83394` vs `origin/master` (F10 BindCreate reverse-this patch on `f7e4b94c`)
- Mode: re-verify of Final Boss `docs/agent/reviews/2026-09-06-pr51-f7e4b94c-final-boss.md` / `docs/agent/reviews/2026-09-06-pr51-f7e4b94c-final-boss-followups.md`
- Model: grok-4.6
- Verdict: ship

## Open bugs (must close before ship)

None.

## Suggestions

- [ ] **F8** — `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:807-811`. Do: keep `Success(BindThis(arg))` now that CLR `object` accepts modeled AST types (`TypeDefinitionExtensions.cs:119-123`), **or** document in CORE / `docs/interpretation/domain-execution-model.md` that simulate typed returns are CreatedChildren-only (`DomainEntityInstance.cs:574-590`) and ignore Success payload (`:680-682`). Update the stale comment at `:803-805` (it still says TypeDefs are not assignable to object after `55b5a588`). Optional lock: a test that would fail if Create/CreateIn existed only as the Success argument (side-effect must remain an assignment — `EffectLoweringPass.cs:901-917`). Not a ship blocker.

## Nits

None.

## Process

- F10 class: an invariant-stating comment that names a sibling (`TryLinkCreateInBackReference` / CreateFines `FindAutoWireBackReference`) is a checklist — ESM `IsBackReference` is self-rel only and is not that sibling. Closed this SHA: BindCreate uses `FindAutoWireBackReference` (`StoreBind.cs:267-281`); F5 asserts Fine.Create args include `this` and reject `ContainsKey("patron")` / `null` (`DomainToCSharpExporterTests.cs:2006-2013`). Keep that oracle; do not let BindCreate reverse regress to dict/null.
- `--treenode-filter` on this host still cannot isolate named tests (zero tests ran for `Export_CreateType_UnambiguousManyRel_EmitsCollectionAdd`, exit 8). Use `#:project` dumps; do not treat “zero tests ran” as green.
- Optional leftover (not a disagreement): no named export test for the F6 domain (`fines` + `waived`). Dump this SHA: BindCreate has neither `_fines.Add` nor `_waived.Add`; Fine.Create uses dict/null. HostAbi `fines=0 reverse=0 waived=0`. Do **not** restore `_lowerStageTransitions`.

## Disposition of prior items (Final Boss @ f7e4b94c, re-read this SHA)

- **F10** — BindCreate reverse Fine.patron was dict/null; `wireUnambiguousBackRef` only hit ESM `IsBackReference` (self-rel); comments claimed HostAbi reverse — **closed**. `StoreBind.cs:114-116` passes `wireUnambiguousBackRef: autoLink`; `:267-281` uses `FindAutoWireBackReference` and emits `ThisReference` for Fine.patron. Dump: `Fine.Create(..., this)` + `_fines.Add`. F5 `:2011-2013` requires `this` and rejects `ContainsKey("patron")` / `null`. CreateFines dump still `Fine.Create(amount, reason, this)`. HostAbi dump `TYPE reverse=1`.
- **F9** — BindCreate outbound Add + F5 BindCreate/AssessByType slice — **still closed**. BindCreate `:123-146` still `_fines.Add` when `outs.Count == 1`; F5 `:2003` and `:2030-2031`; dump AssessByType has no Add.
- **F1** — adapter fail-closed — **still closed**. `DomainEntityInstance.cs:819-825` `Return(DomainResult.Failure(...))`; Capture `CrmDogfoodTests.cs:217-219`; Shop Pay `PipelineTransformationTests.cs:173-203`. Pay dump: `Stripe.Charge` / `no in-process adapter`. Runtime files empty vs `f7e4b94c`.
- **BindThis DomainResult Success/Failure arity (`edd1b8a9`)** — **OK**. Success → `Success()` (`:807-811`); Failure keeps string args (`:812-818`). Create not dropped (assignment before wrap).
- **F8** — Success value discard vs CreatedChildren / export Success(value) — **still open** (suggestion). Independently confirmed; not a ship blocker. No new bug this SHA.
- **Pipeline (simulate create/create-in, `session.Lower`, one tree)** — **holds**. `rg LowerStageTransitions` / `PreprocessRuntimeKeyword` empty in `*.cs`. HostAbi dump: unambiguous `fines=1 reverse=1`; ambiguous `fines=0 reverse=0 waived=0`. Export Type-create reverse now matches (F10).
- **PR 56 emit Add vs HostAbi** — outbound still on BindCreate (F9). Reverse now BindCreate `this` via `FindAutoWireBackReference` (F10), same hook as CreateFines. Did not restore `LowerStageTransitions`.
- **Razor-carried F2–F6** (OnEntry preference, per-invoke re-analysis, Variable rewrite, empty stubs, RequireHttpActionsInModule) — **not re-opened** as ship blockers; not fully re-traced this pass. Foreman: skip Razor.

## Freeze

Filed for **ship**. Never implement from this review. Never merge. Never force-push product.
