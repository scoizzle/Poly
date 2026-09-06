# Follow-ups — PR 51 pipeline (Final Boss re-verify) — 2026-09-06

- Source review: `docs/agent/reviews/2026-09-06-pr51-f7e4b94c-final-boss.md`
- Target: PR 51 SHA `f7e4b94ccab28f86e07025911d3d5423fa60abfd` vs `origin/master` (F9 BindCreate patch on `2a362ea4`)
- Mode: re-verify of Final Boss `docs/agent/reviews/2026-09-06-pr51-2a362ea4-final-boss.md` / `docs/agent/reviews/2026-09-06-pr51-2a362ea4-final-boss-followups.md`
- Model: grok-4.6
- Verdict: not ship

## Open bugs (must close before ship)

- [ ] **F10** — `Poly/DomainModeling/Lowering/DomainToCSharpExporter.StoreBind.cs:270-277`. Do: pass `this` into `Fine.Create` for the unique singular back-nav that `FindAutoWireBackReference` (`DomainToCSharpExporter.Actions.cs:841-856`) would wire — the same hook CreateFines already uses (`Notify.cs:126-141`) — **or** delete `wireUnambiguousBackRef` (`StoreBind.cs:118-120`) and narrow the comments at `:105-106` and `:271-273` so they do not claim `TryLinkCreateInBackReference` parity. `IsBackReference` is self-rel only (`EntityStructureAnalyzer.cs:125-130`); Fine.patron never hits the flag. Proof this SHA (export dump): BindCreate Fine arm is `Fine.Create(amount, reason, values["patron"] or null)` + `this._fines.Add((Fine)created)`; CreateFines is `Fine.Create(amount, reason, this)`; HostAbi dump `TYPE reverse=1`. Harden `Export_CreateType_UnambiguousManyRel_EmitsCollectionAdd` (`DomainToCSharpExporterTests.cs:1972-2023`) so BindCreate’s Fine.Create args include `this` (not `ContainsKey("patron")`); keep AssessByType `DoesNotContain("_fines.Add")`. Optional: export F6 (`fines` + `waived`) and assert BindCreate contains neither `_fines.Add` nor `_waived.Add`. Do **not** restore `_lowerStageTransitions`.

## Suggestions

- [ ] **F8** — `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:807-811`. Do: keep `Success(BindThis(arg))` now that CLR `object` accepts modeled AST types (`TypeDefinitionExtensions.cs:119-123`), **or** document in CORE / `docs/interpretation/domain-execution-model.md` that simulate typed returns are CreatedChildren-only (`DomainEntityInstance.cs:574-590`) and ignore Success payload (`:680-682`). Update the stale comment at `:803-805` (it still says TypeDefs are not assignable to object after `55b5a588`). Optional lock: a test that would fail if Create/CreateIn existed only as the Success argument (side-effect must remain an assignment — `EffectLoweringPass.cs:901-917`). Not a ship blocker.

## Nits

None.

## Process

- An invariant-stating comment that names a sibling (`TryLinkCreateInBackReference`) is a checklist: the flag must fire on that sibling, or the comment is a lie. Recurring class: dual-path auto-link claimed closed while only outbound Add landed; F5 still would not fail if reverse stayed dict/null. Tighten the F5 oracle to Fine.Create args, not only `_fines.Add`.
- `--treenode-filter` on this host still cannot isolate named tests (zero tests ran for the F5 method path). Use `#:project` dumps; do not treat “zero tests ran” as green.

## Disposition of prior items (Final Boss @ 2a362ea4, re-read this SHA)

- **F9** — BindCreate missing Add + F5 whole-unit `Contains("_fines.Add")` false-green via CreateFines — **closed for outbound Add and the AssessByType/BindCreate oracle**. BindCreate `:127-150` emits `_fines.Add` when `outs.Count == 1`; F5 slices BindCreate vs AssessByType (`DomainToCSharpExporterTests.cs:1996-2022`); dump AssessByType has no Add; CreateFines is outside that slice. Guide `_fines.Add` sentence now holds for BindCreate. Reverse portion of the F9 follow-up **not done** — reopened as **F10**.
- **F1** — adapter fail-closed — **still closed**. `DomainEntityInstance.cs:819-825` `Return(DomainResult.Failure(...))`; Capture `CrmDogfoodTests.cs:217-219`; Shop Pay `PipelineTransformationTests.cs:173-203`. Pay dump: `Stripe.Charge` / `no in-process adapter`. Runtime files empty vs `2a362ea4`.
- **BindThis DomainResult Success/Failure arity (`edd1b8a9`)** — **OK**. Success → `Success()` (`:807-811`); Failure keeps string args (`:812-818`). Create not dropped (assignment before wrap).
- **F8** — Success value discard vs CreatedChildren / export Success(value) — **still open** (suggestion). Independently confirmed; not a ship blocker.
- **Pipeline (simulate create/create-in, `session.Lower`, one tree)** — **holds on simulate**. `rg LowerStageTransitions` / `PreprocessRuntimeKeyword` empty in `*.cs`. HostAbi dump: unambiguous `fines=1 reverse=1`; ambiguous `fines=0 reverse=0 waived=0`.
- **PR 56 emit Add vs HostAbi** — outbound **re-homed onto BindCreate** (F9 Add closed). Reverse still export Type-create vs HostAbi / CreateFines (F10). Did not restore `LowerStageTransitions`.
- **Razor-carried F2–F6** (OnEntry preference, per-invoke re-analysis, Variable rewrite, empty stubs, RequireHttpActionsInModule) — **not re-opened** as ship blockers; not fully re-traced this pass. Foreman: skip Razor.

## Freeze

Filed for **not ship**. Never implement from this review. Never merge. Never force-push product.
