# Target architecture doc — follow-ups — 2026-08-17

Source review: [`2026-08-17-target-architecture-review.md`](2026-08-17-target-architecture-review.md).  
Doc: [`docs/plans/domainmodeling-target-architecture-2026-08-16.md`](../../plans/domainmodeling-target-architecture-2026-08-16.md).

Do not implement folder moves from the target doc. Do not flip `PIPELINE-STATUS.md`. Edits belong in the target doc (dated) or a 2026-08-17 revision.

## Resolved (2026-08-17)

F1–F11 addressed in `docs/plans/domainmodeling-target-architecture-2026-08-16.md` (header dated "edited 2026-08-17 for review follow-ups F1–F11"). Nothing else was changed.

- [x] **F1** — Rewrite M1: exporter already returns Syntax via `ToSyntax`; remaining work is split walk + C# idiom, not “stop printf.” (`DomainToCSharpExporter.cs:50-53`, `DomainProgramProjection.cs`)

- [x] **F2** — Pipeline: DomainModeling **does** emit `.poly`. “Never emit host text” ≠ “never emit text.” Place `DomainDslPrinter` on `Language/`.

- [x] **F3** — §8 lock only pipeline + three nouns + contract-fill ≠ library. Un-lock M1/M3/M4 as ADR. Align with cleanup slice 3: `ExecuteStructured` remains until host-ABI.

- [x] **F4** — Homes or delete/later for: `Bootstrap/`, `Queries/`, `DomainCompilation`, `ExpressionMeaning`, annotation syntax types, `DomainExpressionRewriteBase`, lint/cache/behavior analysis types.

- [x] **F5** — Ontology is not “zero behavior”: derived flatten vs dispatch walkers.

- [x] **F6** — Do not invariant “exactly two lowering passes.”

- [x] **F7** — HTTP/DbContext generators stay in `src/` (or pack assemblies), not `Poly/DomainModeling/Libraries/`. In-tree libraries = Temporal + storage facets.

- [x] **F8** — Drop “pure transform.” Session threads tables; analyze stamps bags; evolution is gated mutation.

- [x] **F9** — Relationship to cleanup: slices 1–2 → M2; slice 3 → Comment honesty, not M3.

- [x] **F10** — No folder-move CURRENT from this file; wait for cleanup slices 1–2.

- [x] **F11** — `DomainModeling` → `Poly.Ast`; §2 target-only names; name `DomainExpressionRewriteBase` delete-or-Lowering.

## r2 re-verify (2026-08-17)

Prior F1–F11: **confirmed fixed** in the current doc (see `2026-08-17-target-architecture-review-r2.md`). New items:

- [ ] **F12** — `IStorageSyntaxEmitter` is host-emit (`EmitDbContext` / `EmitApi`). Home = `src/` / vendor, or delete. Not `Meaning/` or in-tree `Libraries/Storage`.
- [ ] **F13** — Ontology list: `DomainMember`, `DomainObject`, `DomainType`/`DomainTypeReference`, `Constraint`, `StageSubscription`, `SubscriptionEventAccess`.
- [ ] **F14** — Locked M2 `Emit` is target; slices 1–2 only require session `Analyze` (+ parse/print). `session.Emit` is later.
- [ ] **F15** — Close O3 as vendors in `src/`; remaining question is catalog membership (mysql extra).
- [ ] **F16** — `ExpressionTypeCheckRegistry` next to the type-check pass, or say why Meaning.
- [ ] **F17** — Session comment: `print(.poly)` vs `contribute artifacts`, not one word `emit`.

## Disposition (prior)

Cleanup-plan follow-ups (`2026-08-15-vision-cleanup-plan-followups.md`) stay on the 08-16 **cleanup** plan. This review does not reopen them.
