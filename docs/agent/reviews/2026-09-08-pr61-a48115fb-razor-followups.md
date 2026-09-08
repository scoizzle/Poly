# Razor follow-ups — PR 61 @ a48115fb — 2026-09-08

Checkable tasks from `docs/agent/reviews/2026-09-08-pr61-a48115fb-razor.md`. Implementer closes boxes with evidence; do not mark done from chat alone.

## Disposition of prior items (`0574ecb5`)

- [x] **F1** — **bug** — **fixed** @ `a48115fb` — `moduleOwnsRequire` skip + Confirm_RequireNot_UnlinkedSection oracle (Succeeded false, ErrorMessage requires linked section, FailedGuards `not_SectionFull`). Soft-false Conditional at `DomainExpressionLoweringPass.cs:150` intentionally retained for bare evaluate.
- [x] **F2** — **bug** — **fixed** @ `a48115fb` — module Failure path + `MapModuleRequireFailure` / `RequireFailure`; CheckIn asserts ErrorMessage `requires a linked 'room'` + FailedGuards `RoomFree`.
- [ ] **F3** — **suggestion** — **still open** — bare EvaluatePolicy / MCP unlinked soft-false vs stale throw claim (`DomainEntityInstanceTests.cs:3153`, OwnedPolicy_Unlinked_FailsClosed).
- [ ] **F4** — **suggestion** — **partial** — first-hop runtime Escalate_MultiHop_UnlinkedReporter added; **second-hop** (linked reporter, unlinked team) still missing.
- [x] **F5** — **suggestion** — **fixed** — nested hop entity clause uses subject entity (`DomainToCSharpExporter.Actions.cs:912-916`); export locks Engineer.
- [x] **F6** — **nit** — **fixed** — renamed to `RelationshipNavigation_LowersToNullForgivingHop`.
- [ ] **F7** — **process** — **still open** — sibling-path checklist for soft-fail dual-path semantics (require + require not + bare + MCP + export + many).

## Open (this tip)

- [ ] **F3** — **suggestion** — Reconcile bare EvaluatePolicy / MCP unlinked contract with PR claims. Tip: false / MCP Success+`false`. Update PR body; choose throw vs soft-false explicitly; rename MCP test if Success=true is intentional.

- [ ] **F4b** — **suggestion** — Add runtime multi-hop **second-hop** unlinked require: linked `reporter`, unlinked `team` → ErrorMessage contains requires linked 'team' (entity Engineer) + FailedGuards. First hop covered by `Escalate_MultiHop_UnlinkedReporter_FailsClosed`.

- [ ] **F8** — **nit** — When mapping `"requires a linked"`, prefer FailedGuards for the policy owning the failing hop instead of all `action.Policies` (`DomainEntityInstance.cs:636-638`).

- [ ] **F7** — **process** — When soft-failing a dual-path semantic (ExistsRelated Conditional), run sibling-path checklist for `require` **and** `require not`, bare EvaluatePolicy, MCP evaluate, export guards, stage policies, and many-cardinality before landing.

## Ship gate

**ship** — prior **F1** and **F2** closed with tests that force module Failure + FailedGuards; no new ship-blockers. Open items are suggestions/nits/process only.
