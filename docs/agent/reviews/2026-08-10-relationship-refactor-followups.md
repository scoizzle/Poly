# Follow-ups — relationship refactor review (2026-08-10)

Review note: [`../agent/reviews/2026-08-10-relationship-refactor-review.md`](../agent/reviews/2026-08-10-relationship-refactor-review.md)

## Resolved (2026-08-10, same commit)

- [x] **R1 (bug)** — `create in Rel` CS1501 export arity fixed: `EffectLoweringPass.CreateEntityInRelationship` now skips collection navs (and back-refs) when building the call, matching `AddCreateNavMethod`'s factory signature — identical arg lists by construction. Verified by compiling the exact `csharp-export-createin-bugs` repro (export → render → `dotnet build`, 0 errors). Guard test: `Export_CreateInTargetWithCollectionNavs_SignatureMatchesCallArity`.
- [x] **R2 (doc honesty)** — `Poly.Mcp/Docs/poly-dsl-guide.md` §0.3 narrowed to what the export does: runtime store link is created; the C# back-ref property is **not** auto-populated (ctor param, `null` unless bound); to-one nav bindings rejected in `create in` initializers. Derived back-ref materialization remains the ADR's future phase.
- [x] **R3 (bridge footgun)** — `Domain.Redistribute` now **appends** to pre-set `Navigations` (`[.. e.Navigations, .. rels]`) instead of replacing. **Bridge RETIRED 2026-08-10:** the 3-arg `Domain` ctor + `Redistribute` removed; production never used it with relationships (all `[]`); 360 test sites migrated to `DomainTestFactory.Create` (test assembly), so `Domain` is strictly `(Name, Types)` and a relationship can only exist on a defined entity — structurally enforced.
- [x] **R4 (multi-source)** — `DomainEntityInstance.ResolveSourceRelationshipOrThrow` reports **all** declaring source entities, not the first.
- [x] **R5 (nit)** — accepted as-is: `ReplaceInEntity` marks the whole entity modified on relationship-content updates, which is correct (the entity changed); per-nav granularity has no current consumer.

## Open

- [x] **R6 (process)** — E-guard: **DONE 2026-08-10** — in-suite Roslyn compile oracle added (`Microsoft.CodeAnalysis.CSharp`); `Export_Compiles_LibraryDomain` + `Export_Compiles_CreateInTargetWithCollectionNavs` compile the rendered export and assert zero errors, so nav-factory arity regressions fail in CI.

## Disposition of prior review items (re-verified)

- 2026-08-10-lowering-findings-review R1–R4: fixed (verified in current source).
- 2026-08-09-csharp-export-review E-guard: **closed** — subsumed by R6 (now a Roslyn oracle).

---

## Addendum — 2026-08-10 metadata-simplification review

Review note: [`../agent/reviews/2026-08-10-metadata-simplification-review.md`](../agent/reviews/2026-08-10-metadata-simplification-review.md)

Re-verify of THIS file (against committed HEAD): R1–R4 confirmed fixed with primary evidence (repro compiles 0 errors; guide §0.3 honest; `Redistribute` appends; multi-source error lists all sources). R5 accepted as-is (correct — the entity changed). R6 still open.

New follow-ups from the simplification review:

- [x] **M1** — `RuleCoverageAnalyzer` transition-presence check **reverted** to the raw effect-walk (`FlattenEffects.Any(e is StageTransitionEffect)`), with a comment explaining why: `ActionCapabilityMetadata.TransitionTargets` is a *resolved-target* view (catalog-resolved stages only), so using it as the presence test silently skipped the coverage hint for actions transitioning to a nonexistent stage. U3a's capability consumption was wrong for this specific check; the effect walk is already performed for coverage, so the raw check is free.
- [x] **M2** — `hasTransport` response-field removal is a tool-surface change; no CHANGELOG exists, but `docs/PROJECT-SUMMARY-FOR-AGENTS.md` listed "storage/transport booleans" — corrected to storage-only, and the pipeline/metadata descriptions updated.
- [x] **M3** — equivalence test added: `Behavior_StageActionEffectivePolicies_EntityStageActionParity` asserts a stage action's `BehaviorAction.Policies` equals `ComposeStagePolicies(entity, stage) + action.Policies` (entity + stage + action), locking the consolidated single-producer composition.
