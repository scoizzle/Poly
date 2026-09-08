# Final Boss follow-ups — PR 61 @ 0253133a — 2026-09-08

Checkable tasks from `docs/agent/reviews/2026-09-08-pr61-0253133a-final-boss.md`. Implementer closes boxes with evidence; do not mark done from chat alone.

- Source review: `docs/agent/reviews/2026-09-08-pr61-0253133a-final-boss.md`
- Target: PR 61 SHA `0253133adaf6564ea7ca2057cab09f47e2f8da18` vs `origin/master` (also vs prior not-ship `a48115fb`)
- Mode: re-verify of Final Boss `docs/agent/reviews/2026-09-08-pr61-a48115fb-final-boss.md` + `2026-09-08-pr61-a48115fb-final-boss-followups.md`
- Model: grok-4.6

## Open bugs (must close before ship)

None. F9 is closed at this SHA.

## Suggestions

- [ ] **F3** — **suggestion** — Reconcile bare EvaluatePolicy / MCP unlinked contract with PR claims. Tip: false / MCP Success+`false` (`DomainEntityInstanceTests.cs:3153-3177`; `SurfaceExtensionDogfoodTests.cs:245-256`). PR body still says GetRelatedOne throw and `LowersToGuardedHop`. Update PR body; choose throw vs soft-false explicitly; rename MCP test if Success=true is intentional.

- [ ] **F4b** — **suggestion** — Add runtime multi-hop **second-hop** unlinked require: linked `reporter`, unlinked `team` → ErrorMessage contains requires linked `'team'` (entity Engineer) + FailedGuards. First hop covered by `Escalate_MultiHop_UnlinkedReporter_FailsClosed` (`PathPrefixRequireFailureTests.cs:125-149`). Export already asserts both hops (`DomainToCSharpExporterTests.cs:1917-1918`).

## Nits

- [ ] **F8** — **nit** — When mapping `"requires a linked"`, prefer FailedGuards for the policy owning the failing hop instead of all `action.Policies` (`DomainEntityInstance.cs:638-640`).

- [ ] **F9-comment** — **nit** — `DomainEntityInstance.Runtime.cs:309-311` still says InvokeAction evaluates guards via EvaluatePolicy first. False for the `moduleOwnsRequire` prelude skip. Tighten so it describes stub-for-typecheck + module Failure first, policy bool via `InvokeNamed` → EvaluatePolicy after path-prefix guards.

## Process

- [ ] **F7** — **process** — When soft-failing or skipping a dual-path semantic (ExistsRelated Conditional **or** moduleOwnsRequire skip), run sibling-path checklist for `require` **and** `require not`, **empty vs non-empty effects**, bare EvaluatePolicy, MCP evaluate, export guards, stage policies, subscriptions/transitions (`actionName` null), and many-cardinality before landing. F9 at `a48115fb` was this class (tests forced the transition-body module path only). This tip added the empty named sibling; keep the checklist.

## Ship gate

**ship** — F9 valid-input fail-open is closed: named `ExecuteEffectList` binds the module Body when `actionName` is set even if `effects.Count == 0` (`DomainEntityInstance.cs:700-715`). Oracles: `EmptyBody_Require_PolicyFalse_FailsClosed`, `EmptyBody_RequireNot_Unlinked_FailsClosed`. F3/F4b/F7/F8 do not block. No new bug this pass.

## Disposition of prior items (`a48115fb` Final Boss / Razor)

- [x] **Razor F1** — **bug** — **closed** @ `0253133a` — non-empty Confirm (`PathPrefixRequireFailureTests.cs:85-122`) and empty Confirm (`:175-197`) both Succeeded false, ErrorMessage requires linked section, FailedGuards `not_SectionFull`. Soft-false Conditional at `DomainExpressionLoweringPass.cs:150` retained for bare evaluate.
- [x] **Razor F2** — **bug** — **closed** @ `0253133a` — CheckIn (`:48-52`) ErrorMessage `requires a linked 'room'` + FailedGuards `RoomFree` via `MapModuleRequireFailure`. Empty-body sibling covered by EmptyBody_RequireNot.
- [ ] **F3** — **suggestion** — **still open** — bare EvaluatePolicy / MCP unlinked soft-false vs stale throw claim.
- [ ] **F4 / F4b** — **suggestion** — **partial / still open** — first-hop runtime covered; second-hop missing.
- [x] **F5** — **suggestion** — **fixed** (prior) — nested hop entity clause uses subject entity (`DomainToCSharpExporter.Actions.cs:912-916`); export locks Engineer (`DomainToCSharpExporterTests.cs:1918`).
- [x] **F6** — **nit** — **fixed** (prior) — renamed to `RelationshipNavigation_LowersToNullForgivingHop` (`DomainExpressionLoweringPassTests.cs:83`).
- [ ] **F7** — **process** — **still open** — checklist item; empty-effects named sibling is now tested, keep the gate.
- [ ] **F8** — **nit** — **still open** — FailedGuards = all `action.Policies` on `"requires a linked"`.
- [x] **F9** — **bug** — **closed** @ `0253133a` — `ExecuteEffectList` `:700-701` early-returns only when `actionName is null`; named path `:704-715` always `BindModuleMethodBody`. Tests `:153-172` and `:175-197`. Residual comment `Runtime.cs:309-311` tracked as F9-comment nit.
