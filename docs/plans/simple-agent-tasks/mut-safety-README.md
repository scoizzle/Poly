# MCP mutation safety — Agent Queue (`mut-safety-*`)

**Parent:** [`../mcp-mutation-safety.md`](../mcp-mutation-safety.md)  
**Gate:** [`mut-safety-gate.md`](./mut-safety-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  
**Note:** Prefer after or with **mcp-minify** so unified `add`/`remove` use the same Evolve lock. Works with either catalog.

**Status:** Ready to admit — **not CURRENT** until human admits.

---

## Objective

1. **No lost updates** on concurrent session evolves.  
2. **Idempotent** structural adds (noop + `was_noop` signal) for agent recovery.  
3. **Clearer rollback** payloads when analysis fails.  
4. Stage list **order** preserved (no random reorder after failed evolve).

### Locks

| ID | Rule |
|----|------|
| L1 | Serialize **writes** per session (pessimistic lock on Evolve path) |
| L2 | Read-only tools stay lock-free |
| L3 | Duplicate structural add → success + `was_noop: true` (not silent wrong error) |
| L4 | No new domain IR; MCP + session store only (+ evolution diagnostics if needed) |
| L5 | Full suite green; existing smoke updated if response shape gains fields |

---

## Task order

| ID | File | Size | Status |
|----|------|------|--------|
| **0** | [`mut-safety-0-inventory.md`](./mut-safety-0-inventory.md) | S | `[ ]` |
| **1** | [`mut-safety-1-session-write-lock.md`](./mut-safety-1-session-write-lock.md) | M | `[ ]` |
| **2** | [`mut-safety-2-concurrent-test.md`](./mut-safety-2-concurrent-test.md) | M | `[ ]` |
| **3** | [`mut-safety-3-idempotent-add.md`](./mut-safety-3-idempotent-add.md) | M | `[ ]` |
| **4** | [`mut-safety-4-rollback-diagnostics.md`](./mut-safety-4-rollback-diagnostics.md) | M | `[ ]` |
| **5** | [`mut-safety-5-stage-order.md`](./mut-safety-5-stage-order.md) | S | `[ ]` |
| **G** | [`mut-safety-gate.md`](./mut-safety-gate.md) | S | `[ ]` |

### Kickoff

```bash
copilot --agent plan-suite-until-done -p "Suite: mut-safety. Mode: until-done."
```

---

## Done definition

Parent acceptance criteria §Acceptance in `mcp-mutation-safety.md` all met + gate green.  
