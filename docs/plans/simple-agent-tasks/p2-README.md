# Multi-hop path-prefix — Agent Queue (`p2-*`)

**Parent:** [`../domain-dsl-absorption-proposals.md`](../domain-dsl-absorption-proposals.md) § P2  
**Orientation:** findings + product guide  
**Gate:** [`p2-gate.md`](./p2-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  

**Status:** **DONE** 2026-08-06 — multi-hop to-one path-prefix parse + preprocess + analysis + goldens + guide.

---

## Objective

Policy (and evaluate) path-prefix over **to-one hop chains** e.g. `loan book Title is "X"` via existing `RelationshipNavigation` tree + EvaluatePolicy preprocess recursion. Analysis validates each hop.

### Rules (locks)

- **To-one only** on bare multi-hop chains (multi-link at a hop → fail closed, same as single-hop).  
- Many in the middle → require quantifiers (`any loans where …`) — **no silent first**.  
- **Assign target** stays local-only (no multi-hop writes).  
- **No** new IR node, SQL joins, or graph query language.

### Success

Store-linked golden: Patron → Loan → Book; policy `loan book Title is "X"`; analysis rejects many-hop without quantifier.

---

## How to pick

1. Only after **p3** suite Done (or human waive).  
2. First `[ ]` in order.  
3. Sequential preferred (analysis + preprocess, then golden, then guide).

### Workflow kickoff

```text
suite=docs/plans/simple-agent-tasks/p2-README.md  mode=until-done
```

---

## Hard rules

| Rule | Why |
|------|-----|
| Preprocess recursion, not new IR | Absorption style B |
| Fail closed multi-link / many middle | Domain fidelity |
| No assign multi-hop | Scope |
| No dates / actors | Parked |
| Guide honesty | Same change |

---

## Task pick order

| ID | File | Size | Status |
|----|------|------|--------|
| **0** | [`p2-0-design-lock.md`](./p2-0-design-lock.md) | S | `[x]` |
| **1** | [`p2-1-analysis-hops.md`](./p2-1-analysis-hops.md) | M | `[x]` |
| **2** | [`p2-2-preprocess-runtime.md`](./p2-2-preprocess-runtime.md) | M | `[x]` |
| **3** | [`p2-3-golden.md`](./p2-3-golden.md) | M | `[x]` |
| **4** | [`p2-4-guide.md`](./p2-4-guide.md) | S | `[x]` |
| **G** | [`p2-gate.md`](./p2-gate.md) | S | `[x]` |

---

## Agent pick (when CURRENT)

```text
NEXT: first [ ] after p3 complete
```

---

## Done definition

1. Multi-hop to-one policy eval green with store links.  
2. Analysis rejects illegal many-middle bare chains.  
3. Guide marks multi-hop path-prefix as product (limits honest).  
4. Build + tests green; gate complete.  
