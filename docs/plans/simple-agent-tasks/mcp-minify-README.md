# MCP catalog minify — Agent Queue (`mcp-minify-*`)

**Parent plan:** [`../mcp-catalog-minify.md`](../mcp-catalog-minify.md) (authority for locks)  
**Gate:** [`mcp-minify-gate.md`](./mcp-minify-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  
**Platform:** [`../../CORE.md`](../../CORE.md) · [`../../../AGENTS.md`](../../../AGENTS.md)  

**Status:** ✅ **DONE 2026-08-08** — tasks 0–7 + gate all `[x]`; tool catalog **46 → 24**; zero `DomainExpressionJsonParser`; unified `add`/`remove` + `apply_dsl` only; suite **1927/1927 green**; pr1 reviewed (no 🔴🟠).

**Open review follow-up queue (2026-08-08):** [`mcp-minify-followups-2026-08-08.md`](./mcp-minify-followups-2026-08-08.md) — **ALL CLOSED 2026-08-08** (B1–B5, S1–S6, N1–N5, P1; suite 1938 green).

---

## Objective

1. Kill **JSON expression bags** (`DomainExpressionJsonParser`) on all MCP tools.  
2. Add **DSL expression fragment** parse API.  
3. Replace per-type `add_*` / `remove_*` with unified **`add` / `remove`** (`kind` + `payload`).  
4. Diet oracle/inspect; docs honest.

### Locks (do not violate)

| Lock | Rule |
|------|------|
| L1 | Expression **body** = product DSL text only — never JSON IR |
| L2 | Only two incremental evolve tools: MCP names `add` and `remove` |
| L3 | Bulk/effects/subscriptions → `apply_dsl` only |
| L4 | No dual-register old `add_entity` tools with new tools |
| L5 | Payload field JSON OK; field named `expression` must be DSL string |
| L6 | Same-kind batch optional; mixed-kind batch → fail closed |
| L7 | Map to existing `DomainEvolution` / Evolve helpers — no new domain IR |

### Success

- Grep: zero `DomainExpressionJsonParser`  
- Grep MCP: zero `[McpServerTool(Name = "add_entity")]` (and siblings)  
- Tools `add` + `remove` green for all kinds in parent §3.3  
- Full suite green; gate complete  

---

## How to pick

1. First task with **Status:** `[ ]` in order **0 → G**.  
2. **One task per agent turn.** Do not skip ahead.  
3. After each task: mark `[x]`, update this table, run verification listed on the task.  
4. Before gate Done: run **pr1** pre-ship review on dirty tree.

### Workflow kickoff

```text
suite=docs/plans/simple-agent-tasks/mcp-minify-README.md  mode=until-done
# copilot --agent plan-suite-until-done -p "Suite: mcp-minify. Mode: until-done."
```

---

## Hard rules

| Rule | Why |
|------|-----|
| Edit only **File ownership** on the task | Prevents scope explosion |
| No new per-type MCP tools | Minify lock |
| No JSON expression IR | Dual-media death |
| Tests for every kind / fail-closed case the task lists | Trivial agents skip otherwise |
| TUnit style | AGENTS.md |
| No `#region` | AGENTS.md |

---

## Task pick order

| ID | File | Size | Status |
|----|------|------|--------|
| **0** | [`mcp-minify-0-inventory.md`](./mcp-minify-0-inventory.md) | S | `[x]` |
| **1** | [`mcp-minify-1-fragment-api.md`](./mcp-minify-1-fragment-api.md) | M | `[x]` |
| **2** | [`mcp-minify-2-oracle-dsl.md`](./mcp-minify-2-oracle-dsl.md) | M | `[x]` |
| **3** | [`mcp-minify-3-add-core-kinds.md`](./mcp-minify-3-add-core-kinds.md) | M | `[x]` |
| **4** | [`mcp-minify-4-add-policy-constraint.md`](./mcp-minify-4-add-policy-constraint.md) | M | `[x]` |
| **5** | [`mcp-minify-5-remove-unified.md`](./mcp-minify-5-remove-unified.md) | M | `[x]` |
| **6** | [`mcp-minify-6-delete-micro-tools.md`](./mcp-minify-6-delete-micro-tools.md) | M | `[x]` |
| **7** | [`mcp-minify-7-catalog-diet-docs.md`](./mcp-minify-7-catalog-diet-docs.md) | S | `[x]` |
| **G** | [`mcp-minify-gate.md`](./mcp-minify-gate.md) | S | `[x]` |

---

## Agent pick (when CURRENT)

```text
NEXT: none — suite DONE 2026-08-08
```

---

## Done definition

1. All tasks `[x]` + gate `[x]`.  
2. Parent plan success checkboxes ticked.  
3. Build + full suite green.  
4. Tree clean after commit (or human holds commit).  
