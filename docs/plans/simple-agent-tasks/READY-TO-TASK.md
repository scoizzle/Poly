# Plans ready for agent micro-tasks

**Date:** 2026-08-07  
**Rule:** One CURRENT suite at a time (master-roadmap).

---

## Ready (solidified — admit to run)

| Priority | Plan | Suite | Why ready |
|----------|------|--------|-----------|
| **1** | [`../grammar-pure-end-state.md`](../grammar-pure-end-state.md) | [`gpure-README.md`](./gpure-README.md) | **DONE 2026-08-07** — pure Grammar product path (see gate) |
| **2** | [`../mcp-catalog-minify.md`](../mcp-catalog-minify.md) | [`mcp-minify-README.md`](./mcp-minify-README.md) | JSON drop + unified add/remove |
| **3** | [`../mcp-mutation-safety.md`](../mcp-mutation-safety.md) | [`mut-safety-README.md`](./mut-safety-README.md) | Session lock + idempotency |
| **4** | [`../p1-temporal-design-lock.md`](../p1-temporal-design-lock.md) | [`p1-README.md`](./p1-README.md) | Temporal pack (bridge OK during gpure) |

**Suggested admit order:** **`gpure` → mcp-minify → mut-safety → p1`** (one stream at a time).

```bash
copilot --agent plan-suite-until-done -p "Suite: gpure. Mode: until-done."
```

---

## Completed suites (archived)

See [`../archive/completed-2026-08-mid/README.md`](../archive/completed-2026-08-mid/README.md)  
and [`../archive/domainmodeling-completed-2026-08/README.md`](../archive/domainmodeling-completed-2026-08/README.md).

Includes: dogfood, amu, p4, coh, p3, p2, grammar/GIP, older DAS/SPE/… suites.

---

## Design locks — not implement queues

| Doc | Why |
|-----|-----|
| (gpure suite is live above) | Direction lock + tasks |
| instance-commit-and-outbox | Needs durable host |
| customer-trust-proof-map | Living index |
| absorption matrix | Pick one P* → solidify suite first |

---

## How to solidify a new plan

1. Parent plan: locks + exit + ownership.  
2. `*-README.md` + numbered tasks (exact steps, verify, file ownership).  
3. Gate + pr1.  
4. Register key in `.github/agents/plan-suite-until-done.agent.md`.  
5. Link here + [`../README.md`](../README.md).  
