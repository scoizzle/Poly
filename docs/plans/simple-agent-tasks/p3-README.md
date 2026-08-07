# Action return types honesty — Agent Queue (`p3-*`)

**Parent:** [`../domain-dsl-absorption-proposals.md`](../domain-dsl-absorption-proposals.md) § P3  
**Orientation:** [`../domainmodeling-cohesion-and-metadata-findings.md`](../domainmodeling-cohesion-and-metadata-findings.md)  
**Product guide:** `Poly.Mcp/Docs/poly-dsl-guide.md`  
**Gate:** [`p3-gate.md`](./p3-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  

**Status:** Ready — **CURRENT first** after pipeline (admit before P2).  
**Not:** P1 temporal (parked research). **Not:** full type system / generics.

---

## Objective

Harden **one end-to-end return shape** for actions with `-> Type`: inventory → analysis fail-closed when declared result has no producer → runtime/MCP honesty → guide.

Parser/print already emit `-> Type`. Runtime result plumbing may still be success/stage only.

### Thin vertical (success)

1. Inventory what `-> Entity` / `-> Text` / void mean today (invoke, create, MCP).  
2. One golden: action with `-> T` returns a usable value (created instance or primitive) on product path.  
3. Analysis: `-> T` without producing effect → **error** (fail closed).  
4. Guide documents what agents actually get back.

---

## How to pick

1. First `[ ]` in order (0 → 4).  
2. Sequential.  
3. Pre-ship before gate Done.

### Workflow kickoff

```text
suite=docs/plans/simple-agent-tasks/p3-README.md  mode=until-done
# Copilot: copilot --agent plan-suite-until-done -p "Suite: p3. Mode: until-done."
```

---

## Hard rules

| Rule | Why |
|------|-----|
| One return shape vertical | No generics / unions / “last expr is return” without analysis |
| Fail closed declared `-> T` | No silent void success |
| Guide same change as behavior | Honesty |
| No multi-hop / dates / actors | Separate suites |
| File ownership | Respect task lists |

---

## Task pick order

| ID | File | Size | Status |
|----|------|------|--------|
| **0** | [`p3-0-inventory.md`](./p3-0-inventory.md) | S | `[ ]` |
| **1** | [`p3-1-analysis-require-producer.md`](./p3-1-analysis-require-producer.md) | M | `[ ]` |
| **2** | [`p3-2-runtime-golden.md`](./p3-2-runtime-golden.md) | M | `[ ]` |
| **3** | [`p3-3-mcp-honesty.md`](./p3-3-mcp-honesty.md) | S | `[ ]` |
| **4** | [`p3-4-guide.md`](./p3-4-guide.md) | S | `[ ]` |
| **G** | [`p3-gate.md`](./p3-gate.md) | S | `[ ]` |

---

## Agent pick (when CURRENT)

```text
NEXT: first [ ] above
THEN: admit p2 multi-hop after p3 gate
```

---

## Done definition

1. Inventory doc or task notes cite real types/paths.  
2. Analysis error when `-> T` lacks producer.  
3. At least one e2e golden (runtime and/or MCP) returns a value for `-> T`.  
4. Guide honest.  
5. Build + tests green; gate complete.  
