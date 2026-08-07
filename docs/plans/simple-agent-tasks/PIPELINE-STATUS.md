# Pipeline status

**Updated:** 2026-08-06 (post phenomenal review follow-ups F1–F7, P1 **closed**)  
**Current stage:** `(none — all suites complete)`  
**Last task:** _pipeline complete; review follow-ups F1–F7 + P1 resolved_  
**Blocker:** none

## Stages

| Stage | Status |
|-------|--------|
| dogfood (wave 2 S4–S6) | `done` — discovery PASS×3, G-S6-1 fix in `dogfood-fix-*` reports |
| amu | `done` — W0–W4 + gate G1–G7 PASS; **F1/F3/F4 closed** (catalog+RLM parity, fail-closed bags, facts resolve, R14 fixed) |
| p4 | `done` — any/all parse/goldens + gate G1–G5 PASS; **F5 closed** (singular + any/all is an error, not a warning) |
| coh | `done` — Runtime/ + dispatch + evolution helpers (folder; namespace still DomainModeling) |

**Review:** [`../../agent/reviews/2026-08-06-pipeline-amu-p4-coh-dogfood.md`](../../agent/reviews/2026-08-06-pipeline-amu-p4-coh-dogfood.md)  
**Follow-ups:** [`pipeline-followups-2026-08-06.md`](./pipeline-followups-2026-08-06.md) — F1–F7 + P1 closed 2026-08-06

## How to run (Copilot CLI)

```bash
copilot --agent domainmodeling-backlog -p "Execute SUITE-OF-SUITES until all stages complete."
```

See [`SUITE-OF-SUITES.md`](./SUITE-OF-SUITES.md).
