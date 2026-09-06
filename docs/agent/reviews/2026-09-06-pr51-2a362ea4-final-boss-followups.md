# Follow-ups — PR 51 pipeline (Final Boss re-verify) — 2026-09-06

- Source review: `docs/agent/reviews/2026-09-06-pr51-2a362ea4-final-boss.md`
- Target: PR 51 SHA `2a362ea45bd8d91f09d806d3c3f5793f7cd19037` vs `origin/master` (merge of `8b995997` + PR 54/56)
- Mode: re-verify of Final Boss `docs/agent/reviews/2026-09-06-pr51-8b995997-final-boss.md` / `docs/agent/reviews/2026-09-06-pr51-8b995997-final-boss-followups.md`
- Model: grok-4.6
- Verdict: not ship

## Open bugs (must close before ship)

- [ ] **F9** — `Poly/DomainModeling/Lowering/DomainToCSharpExporter.StoreBind.cs:97-122`. Do: re-home unambiguous Type-create auto-link onto `BindCreate` (same `outs.Count == 1` rule as `DomainEntityInstance.HostAbi.cs:678-688`, including reverse), **or** retract `Poly.Mcp/Docs/poly-dsl-guide.md:73-79` ("C# export likewise emits `_fines.Add`") and rewrite `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs:1972-1992` so it fails unless the **AssessByType** body wires the collection (not `CreateFines` / `AttachFines` at `DomainToCSharpExporter.Notify.cs:219-224`). Do **not** restore `_lowerStageTransitions` emit Add (`EffectLoweringPass` on master `:662-675`) — that is a consumer lowering flag. Dead Add-skip at `DomainToCSharpExporter.Actions.cs:277-284` should match whatever BindCreate/AssessByType actually emit. Proof this SHA: generated AssessByType is `this.Create` + `Success((Fine)created1)` with no Add; `Contains("_fines.Add")` is true only because create-in factories exist.

## Suggestions

- [ ] **F8** — `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:807-811`. Do: keep `Success(BindThis(arg))` now that CLR `object` accepts modeled AST types (`TypeDefinitionExtensions.cs:119-123`), **or** document in CORE / `docs/interpretation/domain-execution-model.md` that simulate typed returns are CreatedChildren-only (`DomainEntityInstance.cs:574-590`) and ignore Success payload (`:680-682`). Update the stale comment at `:803-805` (it still says TypeDefs are not assignable to object after `55b5a588`). Optional lock: a test that would fail if Create/CreateIn existed only as the Success argument (side-effect must remain an assignment — `EffectLoweringPass.cs:901-917`). Not a ship blocker.

## Nits

None.

## Process

- Merge of a dual-path test + product-guide sentence must **force the named sibling** (AssessByType / BindCreate), not a compilation-unit substring that another factory already satisfies. Recurring class: test theater after merge-master. Tighten the uncommitted-review gate / this protocol's sibling-path checklist for "imported test, dropped implementation."
- `--treenode-filter` on this host still cannot isolate named tests (TUnit: at most 1 argument; class/method globs ran 0 tests). Use `#:project` dumps or fix the filter; do not treat "zero tests ran" as green.

## Disposition of prior items (Final Boss @ 8b995997, re-read this SHA)

- **F1** — adapter fail-closed — **still closed**. `DomainEntityInstance.cs:819-825` `Return(DomainResult.Failure(...))`; Capture `CrmDogfoodTests.cs:217-219` `Succeeded == false` + `Billing.Charge`; Shop Pay `PipelineTransformationTests.cs:173-203`. Pay dump: `Stripe.Charge` / `no in-process adapter`. Pattern order keeps adapter after Success/Failure arity arms. Runtime files empty vs `8b995997`.
- **BindThis DomainResult Success/Failure arity (`edd1b8a9`)** — **OK**. Success → `Success()` (`:807-811`); Failure keeps string args (`:812-818`). Create not dropped (assignment before wrap). Merge kept Success TypeCast (`Actions.cs:301-308`).
- **F8** — Success value discard vs CreatedChildren / export Success(value) — **still open** (suggestion). Independently confirmed; not a ship blocker.
- **Pipeline (simulate create/create-in, `session.Lower`, one tree)** — **holds on simulate**. `rg LowerStageTransitions` / `PreprocessRuntimeKeyword` empty in `*.cs`. Named invoke uses `GetOrLower` + BindThis. `CreateCore` `:227` auto-links Fine Type (same helper as `CreateChildInstance` `:640`). HostAbi dump: unambiguous `fines=1 reverse=1`; ambiguous `fines=0 waived=0`.
- **PR 52 F5 / PR 56 emit Add** — **reopened as F9**. Master closed export Type-create Add on Stay.Create; this merge kept `this.Create` and imported the F5 test + guide claim without BindCreate auto-link. PR 52 follow-ups copied into this merge (`docs/agent/reviews/2026-09-06-pr52-d07aabf3-final-boss-followups.md`) still describe EffectLowering `:610-624` — stale line numbers on this tree.
- **Razor-carried F2–F6** (OnEntry preference, per-invoke re-analysis, Variable rewrite, empty stubs, RequireHttpActionsInModule) — **not re-opened** as ship blockers; not fully re-traced this pass.

## Freeze

Filed for **not ship**. Never implement from this review. Never merge. Never force-push product.
