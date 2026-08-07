# Subscription quantifiers authoring — Agent Queue (`p4-*`)

**Parent:** [`../domain-dsl-absorption-proposals.md`](../domain-dsl-absorption-proposals.md) § P4  
**Orientation:** [`../domainmodeling-cohesion-and-metadata-findings.md`](../domainmodeling-cohesion-and-metadata-findings.md)  
**Product guide:** `Poly.Mcp/Docs/poly-dsl-guide.md`  
**Gate:** [`p4-gate.md`](./p4-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  

**Status:** **DONE** — 2026-08-06 (p4-0 … p4-4 + gate; see [`p4-gate.md`](./p4-gate.md)).

---

## Objective

Product DSL authors `when any|all Rel Stage…` (default omit = Each). Runtime already dispatches Each/Any/All — **zero new runtime path**. Parse/print + analysis cardinality + goldens + guide honesty.

### Grammar lock

```text
when [any|all] Rel Stage[, Stage…] [as name] { effects }
```

Omit quantifier = `Each` (current product).

---

## How to pick

1. First `[ ]` in order (0 → 4).  
2. Sequential preferred (parser then analysis then tests then guide).  
3. Pre-ship before suite Done.

### Workflow kickoff

```text
suite=docs/plans/simple-agent-tasks/p4-README.md  mode=until-done
```

---

## Hard rules

| Rule | Why |
|------|-----|
| No new runtime dispatch algorithm | Store already handles Any/All |
| Guide same change as behavior | Honesty |
| Peer `as` remains valid with any/all | Document set-state-after-transition |
| Fail closed singular + Any/All | Analysis already warns — keep |
| No dates / multi-hop / actors | Out of suite |

---

## Task pick order

| ID | File | Size | Status |
|----|------|------|--------|
| **0** | [`p4-0-design-lock.md`](./p4-0-design-lock.md) | S | `[x]` |
| **1** | [`p4-1-parse-print.md`](./p4-1-parse-print.md) | M | `[x]` |
| **2** | [`p4-2-analysis.md`](./p4-2-analysis.md) | S | `[x]` |
| **3** | [`p4-3-goldens.md`](./p4-3-goldens.md) | M | `[x]` |
| **4** | [`p4-4-guide.md`](./p4-4-guide.md) | S | `[x]` |
| **G** | [`p4-gate.md`](./p4-gate.md) | S | `[x]` |

---

## Agent pick (when CURRENT)

```text
NEXT: first [ ] above
```

---

## Done definition

1. Round-trip any/all/each; Each regression green.  
2. Store golden: Any fires once when set state matches.  
3. Guide documents syntax + empty/singular rules.  
4. Build + tests green; gate complete.  
