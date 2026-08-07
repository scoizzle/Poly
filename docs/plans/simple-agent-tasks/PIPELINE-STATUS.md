# Pipeline status

**Updated:** 2026-08-06  
**Current stage:** `p3` (return types)  
**Last task:** _post-pipeline admit: P3 then P2; P1 research parked_  
**Blocker:** none

## Stages

| Stage | Status |
|-------|--------|
| dogfood → amu → p4 → coh | `done` (historical SUITE-OF-SUITES) |
| **p3** return types | `in_progress` / CURRENT — [`p3-README.md`](./p3-README.md) |
| **p2** multi-hop | `pending` after p3 — [`p2-README.md`](./p2-README.md) |
| **p1** temporal | `research only` — [`../p1-temporal-research.md`](../p1-temporal-research.md) |

**Review:** [`../../agent/reviews/2026-08-06-pipeline-amu-p4-coh-dogfood.md`](../../agent/reviews/2026-08-06-pipeline-amu-p4-coh-dogfood.md)  
**Follow-ups:** [`pipeline-followups-2026-08-06.md`](./pipeline-followups-2026-08-06.md) — F1–F7 + P1 closed 2026-08-06

## How to run (Copilot CLI)

```bash
copilot --agent domainmodeling-backlog -p "Execute SUITE-OF-SUITES until all stages complete."
```

See [`SUITE-OF-SUITES.md`](./SUITE-OF-SUITES.md).
