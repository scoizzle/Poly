# Vision cleanup plan — follow-ups — 2026-08-15

Source review: [`2026-08-15-vision-cleanup-plan-review.md`](2026-08-15-vision-cleanup-plan-review.md).  
Plan: [`docs/plans/domainmodeling-vision-cleanup-2026-08-15.md`](../../plans/domainmodeling-vision-cleanup-2026-08-15.md).

Superseding plan: [`docs/plans/domainmodeling-vision-cleanup-2026-08-16.md`](../../plans/domainmodeling-vision-cleanup-2026-08-16.md). Do not flip `PIPELINE-STATUS.md` CURRENT from this file.

## Disposition (F1–F13 / P1)

Addressed by rewriting the plan (2026-08-16 three slices), not by executing waves.

- [x] **F1** — C1/C2 split is now slice 3 (keep `ExecuteStructured`) vs later suite (host-ABI). C2 is not in this plan.
- [x] **F2** — Slice 2: session `StoragePass` gets maps before compiler re-run dies. No “byte-identical.”
- [x] **F3** — Slice 1 first test: unknown id throws; no `failOnUnknown: false` on session.
- [x] **F4** — Honesty table + slice 3 “still a lie”: emit-mode context, not one bool.
- [x] **F5** — MCP merge is **not this plan** (later work table).
- [x] **F6** — Slice 2 kill list is Evolution + `McpSessionStore` call sites.
- [x] **F7** — Honesty + later-work: `CreateInputs` / Guid libraries.
- [x] **F8** — Slice 1: MySQL is not a CLI seed.
- [x] **F9** — Explicit not-this-plan list on the 16th plan.
- [x] **F10** — Rename parked.
- [x] **F11** — Slice 3: update goldens; no identical-C#.
- [x] **F12** — `uses http` / exporter walks not in this plan.
- [x] **P1** — 2026-08-16 honesty table recomputed; Wave C AC not copied.

## Open (execution — only after admission)

- [x] **E1** — Slice 1 landed 2026-08-17: unknown `uses` throws; `DomainHost*` / `FromInputs` / `failOnUnknown` gone.
- [x] **E2** — Slice 2 landed 2026-08-17: `session.Analyze` is the Evolution/MCP/compiler door; no compiler `new StoragePass(`.
- [x] **E3** — Slice 3 landed 2026-08-17: `new Comment(` empty under `Poly/DomainModeling`; `ExecuteStructured` kept.

## Disposition (prior)

No prior review of this plan. Cleanup-inventory 2026-08-15 remaining Host/lint items: **still open** in the tree; this plan covers Host nouns and drops lint without saying so (F9).
