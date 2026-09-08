# Final Boss follow-ups — PR 61 @ a48115fb — 2026-09-08

Checkable tasks from `docs/agent/reviews/2026-09-08-pr61-a48115fb-final-boss.md`. Implementer closes boxes with evidence; do not mark done from chat alone.

- Source review: `docs/agent/reviews/2026-09-08-pr61-a48115fb-final-boss.md`
- Target: PR 61 SHA `a48115fb397f8f135436dae7e0b4cdff61b5d0f5` vs `origin/master` (also vs prior not-ship `0574ecb5`)
- Mode: re-verify of Razor `docs/agent/reviews/2026-09-08-pr61-a48115fb-razor.md` + `2026-09-08-pr61-a48115fb-razor-followups.md`
- Model: grok-4.6

## Open bugs (must close before ship)

- [ ] **F9** — **bug** — Named `InvokeAction` with `moduleOwnsRequire` skips action-policy EvaluatePolicy (`DomainEntityInstance.cs:522-531`) then `ExecuteEffectList` returns null when `effects.Count == 0` (`:695-696`) **before** binding the module Body that still contains require Failure + Success (`BuildActionBodyWithGuards` `DomainToCSharpExporter.Actions.cs:232-272`). Legal `action require P { }` (DMBEH001-compliant; `PipelineMergeMetadataTests.cs:233`) and empty `require not` + unlinked path-prefix both return **Succeeded=true**. Do: run the module Body for named `actionName` even when effects are empty (or do not skip EvaluatePolicy unless that Body will execute). Tests: (1) `Submit: action require ActivePolicy { }` / IsActive=false → Succeeded false + FailedGuards or blocked-by-policy; (2) empty `require not SectionFull` unlinked → ErrorMessage contains `requires a linked 'section'` (same contract as `Confirm_RequireNot_UnlinkedSection_FailsClosed_DoesNotThrow` which has a transition). Tighten `DomainEntityInstance.Runtime.cs:309-311` so it does not claim EvaluatePolicy always runs first.

## Suggestions

- [ ] **F3** — **suggestion** — Reconcile bare EvaluatePolicy / MCP unlinked contract with PR claims. Tip: false / MCP Success+`false` (`DomainEntityInstanceTests.cs:3153-3177`; `SurfaceExtensionDogfoodTests.cs:245-256`). PR body still says GetRelatedOne throw and `LowersToGuardedHop`. Update PR body; choose throw vs soft-false explicitly; rename MCP test if Success=true is intentional.

- [ ] **F4b** — **suggestion** — Add runtime multi-hop **second-hop** unlinked require: linked `reporter`, unlinked `team` → ErrorMessage contains requires linked `'team'` (entity Engineer) + FailedGuards. First hop covered by `Escalate_MultiHop_UnlinkedReporter_FailsClosed` (`PathPrefixRequireFailureTests.cs:125-149`). Export already asserts both hops (`DomainToCSharpExporterTests.cs:1917-1918`).

## Nits

- [ ] **F8** — **nit** — When mapping `"requires a linked"`, prefer FailedGuards for the policy owning the failing hop instead of all `action.Policies` (`DomainEntityInstance.cs:636-638`).

## Process

- [ ] **F7** — **process** — When soft-failing or skipping a dual-path semantic (ExistsRelated Conditional **or** moduleOwnsRequire skip), run sibling-path checklist for `require` **and** `require not`, **empty vs non-empty effects**, bare EvaluatePolicy, MCP evaluate, export guards, stage policies, and many-cardinality before landing. F9 is this class: tests forced the transition-body module path only.

## Ship gate

**not ship** — F9 is a new valid-input fail-open on the same require semantic this PR claims to close. Razor F1/F2 remain closed **only** for non-empty-effect Domain-bound invoke. Do not merge until F9 has a failing test then a fix. F3/F4b/F7/F8 do not block after F9.

## Disposition of prior items (`a48115fb` Razor / `0574ecb5` not-ship)

- [x] **Razor F1** — **bug** — **closed on non-empty-effect path** @ `a48115fb` — `moduleOwnsRequire` skip + `Confirm_RequireNot_UnlinkedSection` (`PathPrefixRequireFailureTests.cs:85-122`) Succeeded false, ErrorMessage requires linked section, FailedGuards `not_SectionFull`. Soft-false Conditional at `DomainExpressionLoweringPass.cs:150` retained for bare evaluate. **Sibling fail-open remains as F9** for empty effects.
- [x] **Razor F2** — **bug** — **closed on non-empty-effect path** @ `a48115fb` — CheckIn (`:48-52`) ErrorMessage `requires a linked 'room'` + FailedGuards `RoomFree` via `MapModuleRequireFailure`. **Same F9 hole** if the action body is empty.
- [ ] **F3** — **suggestion** — **still open** — bare EvaluatePolicy / MCP unlinked soft-false vs stale throw claim.
- [ ] **F4 / F4b** — **suggestion** — **partial / still open** — first-hop runtime covered; second-hop missing.
- [x] **F5** — **suggestion** — **fixed** — nested hop entity clause uses subject entity (`DomainToCSharpExporter.Actions.cs:912-916`); export locks Engineer (`DomainToCSharpExporterTests.cs:1918`).
- [x] **F6** — **nit** — **fixed** — renamed to `RelationshipNavigation_LowersToNullForgivingHop` (`DomainExpressionLoweringPassTests.cs:83`).
- [ ] **F7** — **process** — **still open** — empty-effects sibling was not on the Razor checklist; F9 is the miss.
- [ ] **F8** — **nit** — **still open** — FailedGuards = all `action.Policies` on `"requires a linked"`.
- [ ] **F9** — **bug** — **open (this pass)** — empty-effects named invoke skips module require.
