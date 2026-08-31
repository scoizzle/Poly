# Plans ready for agent micro-tasks

**Date:** 2026-08-31  
**Rule:** Trunk is `master`. Parallel streams with exclusive file ownership.  
**CURRENT truth:** [`PIPELINE-STATUS.md`](./PIPELINE-STATUS.md) (only) — **CURRENT: `interpretation-language-engine (ile-0/1/2 VM↔LINQ parity gaps closed; ile-3 functions green)`**. ile-gate is not closed; do not invent a second CURRENT.

---

## Status snapshot (mirrors PIPELINE-STATUS)

| Priority | Suite | Status |
|----------|--------|--------|
| CURRENT | [`interpretation-language-engine-README.md`](./interpretation-language-engine-README.md) | **CURRENT** — ile-0/1/2/3 green; ile-gate not closed. Mirror [`PIPELINE-STATUS.md`](./PIPELINE-STATUS.md). |
| — | [`gpure-README.md`](./gpure-README.md) | ✅ DONE 2026-08-07 |
| — | [`mcp-minify-README.md`](./mcp-minify-README.md) | ✅ DONE 2026-08-08 |
| — | emit-session | ✅ DONE 2026-08-24 (CompileMode seed-only honesty). Remaining lies: Temporal Meaning unused; RuntimeAnalysisCache core-catalog reopen. |
| — | rewrite-to-master | ✅ DONE PR 26. Trunk is `master`. Plan: [`rewrite-to-master-2026-08-25.md`](./rewrite-to-master-2026-08-25.md). |
| parked | [`mut-safety-README.md`](./mut-safety-README.md) | THEN in PIPELINE-STATUS — not admitted |
| 3a | [`p1-README.md`](./p1-README.md) | After pack-2-gate |

Admit order lives only in [`PIPELINE-STATUS.md`](./PIPELINE-STATUS.md) (`THEN` / `PARKED`). Do not treat mut-safety as admit-next.

---

## Completed suites (archived)

See [`../archive/completed-2026-08-mid/README.md`](../archive/completed-2026-08-mid/README.md)  
and [`../archive/domainmodeling-completed-2026-08/README.md`](../archive/domainmodeling-completed-2026-08/README.md).

Includes: dogfood, amu, p4, coh, p3, p2, grammar/GIP, older DAS/SPE/… suites, plus live DONE gpure + mcp-minify (tasks remain under `simple-agent-tasks/` until a bulk archive pass).

---

## Design locks — not implement queues

| Doc | Why |
|-----|-----|
| instance-commit-and-outbox | Needs durable host |
| customer-trust-proof-map | Living index |
| absorption matrix | Pick one P* → solidify suite first |
| [`../domainmodeling-e2e-representation-2026-08-13.md`](../domainmodeling-e2e-representation-2026-08-13.md) | Parked parent |
| [`e2e-README.md`](./e2e-README.md) | **Fleet task pack** — wave DAG + 12 slice READMEs; admit one wave |
| [`../fleet-eval-fixes-2026-08-12.md`](../fleet-eval-fixes-2026-08-12.md) | Probe checklist; do not CURRENT beside overlapping `e2e-*` |
| [`../dead-dual-inventory-2026-08-08.md`](../dead-dual-inventory-2026-08-08.md) | Kill list for Validation / Text — not CURRENT work |

---

## How to solidify a new plan

1. Parent plan: locks + exit + ownership.  
2. `*-README.md` + numbered tasks (exact steps, verify, file ownership).  
3. Gate + pr1.  
4. Register key in `.github/agents/plan-suite-until-done.agent.md`.  
5. Link here + [`../README.md`](../README.md).  
6. On suite **DONE**: update **PIPELINE-STATUS** Agent pick in the **same** change.  
