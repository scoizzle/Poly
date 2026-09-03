# Plans ready for agent micro-tasks

**Date:** 2026-09-01  
**Rule:** Trunk is `master`. Parallel streams with exclusive file ownership.  
**CURRENT truth:** [`PIPELINE-STATUS.md`](./PIPELINE-STATUS.md) (only) — **CURRENT: `create/create-in`**. Do not invent a second CURRENT.

---

## Status snapshot (mirrors PIPELINE-STATUS)

| Priority | Suite | Status |
|----------|--------|--------|
| CURRENT | create/create-in | **CURRENT** — host-ABI remaining store effects. Mirror [`PIPELINE-STATUS.md`](./PIPELINE-STATUS.md). |
| parked | [`mut-safety-README.md`](./mut-safety-README.md) | THEN in PIPELINE-STATUS — not admitted |
| parked | [`gcyc-README.md`](./gcyc-README.md) | Remaining G4 unparse — THEN, not CURRENT |
| parked | [`e2e-README.md`](./e2e-README.md) | Admit one wave |
| parked | [`pack-README.md`](./pack-README.md) | pack-2 / 3 — extension model superseded |
| 3a | [`p1-README.md`](./p1-README.md) | After pack-2-gate |

Admit order lives only in [`PIPELINE-STATUS.md`](./PIPELINE-STATUS.md) (`THEN` / `PARKED`). Do not treat mut-safety as admit-next.

---

## Completed suites (archived)

See [`../archive/completed-2026-08-late/README.md`](../archive/completed-2026-08-late/README.md)  
and [`../archive/completed-2026-08-mid/README.md`](../archive/completed-2026-08-mid/README.md)  
and [`../archive/domainmodeling-completed-2026-08/README.md`](../archive/domainmodeling-completed-2026-08/README.md).

Includes: gpure, mcp-minify, ile, pack-1, rewrite-to-master, grammar-revision, dead-dual, vision-cleanup; plus earlier dogfood, amu, p4, coh, p3, p2, grammar/GIP, DAS/SPE/… suites.

---

## Design locks — not implement queues

| Doc | Why |
|-----|-----|
| instance-commit-and-outbox | Needs durable host |
| customer-trust-proof-map | Living index |
| absorption matrix | Pick one P* → solidify suite first |
| [`../domainmodeling-e2e-representation-2026-08-13.md`](../domainmodeling-e2e-representation-2026-08-13.md) | Parked parent |
| [`e2e-README.md`](./e2e-README.md) | **Fleet task pack** — wave DAG + slice READMEs; admit one wave |
| [`../fleet-eval-fixes-2026-08-12.md`](../fleet-eval-fixes-2026-08-12.md) | Probe checklist; do not CURRENT beside overlapping `e2e-*` |

---

## How to solidify a new plan

1. Parent plan: locks + exit + ownership.  
2. `*-README.md` + numbered tasks (exact steps, verify, file ownership).  
3. Gate + pr1.  
4. Register key in `.github/agents/plan-suite-until-done.agent.md`.  
5. Link here + [`../README.md`](../README.md).  
6. On suite **DONE**: update **PIPELINE-STATUS** Agent pick in the **same** change. Archive the suite under `docs/plans/archive/` in a later hygiene pass — do not leave DONE task files in this folder.
