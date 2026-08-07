# Pipeline status

**Updated:** 2026-08-06  
**Current stage:** `(none)`  
**Last task:** _p3 + p2 orchestrated and gates closed_  
**Blocker:** none

## Stages

| Stage | Status |
|-------|--------|
| dogfood → amu → p4 → coh | `done` |
| **p3** return types | `done` — DMEFF009, ResultInstance, MCP returnInstanceId |
| **p2** multi-hop | `done` — parse + preprocess hop chain + analysis + goldens |
| **p1** temporal | `research only` — [`../p1-temporal-research.md`](../p1-temporal-research.md) |

**Review:** [`../../agent/reviews/2026-08-06-pipeline-amu-p4-coh-dogfood.md`](../../agent/reviews/2026-08-06-pipeline-amu-p4-coh-dogfood.md)  
**Follow-ups:** [`pipeline-followups-2026-08-06.md`](./pipeline-followups-2026-08-06.md) — F1–F7 + P1 closed 2026-08-06

## How to run (Copilot CLI)

```bash
copilot --agent domainmodeling-backlog -p "Execute SUITE-OF-SUITES until all stages complete."
```

See [`SUITE-OF-SUITES.md`](./SUITE-OF-SUITES.md).
