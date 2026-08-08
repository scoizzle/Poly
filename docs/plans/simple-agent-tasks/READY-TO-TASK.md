# Plans ready for agent micro-tasks

**Date:** 2026-08-08  
**Rule:** One CURRENT suite at a time.  
**CURRENT truth:** [`PIPELINE-STATUS.md`](./PIPELINE-STATUS.md) (only).

---

## Status snapshot (mirrors PIPELINE-STATUS)

| Priority | Suite | Status |
|----------|--------|--------|
| — | [`gpure-README.md`](./gpure-README.md) | ✅ DONE 2026-08-07 |
| — | [`mcp-minify-README.md`](./mcp-minify-README.md) | ✅ DONE 2026-08-08 |
| **1 admit** | [`mut-safety-README.md`](./mut-safety-README.md) | Ready — **admit next** |
| **2** | [`p1-README.md`](./p1-README.md) | Ready after mut-safety |

```bash
copilot --agent plan-suite-until-done -p "Suite: mut-safety. Mode: until-done."
```

**Suggested admit order (historical):** gpure → mcp-minify → **mut-safety** → p1.

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
| [`../dead-dual-inventory-2026-08-08.md`](../dead-dual-inventory-2026-08-08.md) | Kill list for Validation / Text — not CURRENT work |

---

## How to solidify a new plan

1. Parent plan: locks + exit + ownership.  
2. `*-README.md` + numbered tasks (exact steps, verify, file ownership).  
3. Gate + pr1.  
4. Register key in `.github/agents/plan-suite-until-done.agent.md`.  
5. Link here + [`../README.md`](../README.md).  
6. On suite **DONE**: update **PIPELINE-STATUS** Agent pick in the **same** change.  
