# e2e-0 — Honesty (docs + leftover grammar)

**Parent:** [`../domainmodeling-e2e-representation-2026-08-13.md`](../domainmodeling-e2e-representation-2026-08-13.md) § Slice 0  
**Fleet coordinator:** [`e2e-README.md`](./e2e-README.md)  
**Wave:** 1 · **Parallel with:** e2e-p, e2e-g0  
**Gate:** [`e2e-0-gate.md`](./e2e-0-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)

**Status:** `[x]` Done 2026-08-13 (opencode, fleet agent). Gate green — build 0/0, 2065/2065, pr1 clean.

## Objective

Shipped docs match the code. Deleted 2026-08-10 effects are gone from inventories. Delete-effect grammar does not leak an internal error. L3 wording says params are bare identifiers that analysis must treat as parameters.

## Locks

- No new DSL. No uniqueness/export/runtime behavior (except delete-pattern parse diagnostic).
- Guide: one honesty sweep here. Later slices only append their “now shipped” bullet.

## Task order

| ID | File | Size | Status |
|----|------|------|--------|
| **1** | [`e2e-0-1-guide.md`](./e2e-0-1-guide.md) | M | `[x]` |
| **2** | [`e2e-0-2-inventories.md`](./e2e-0-2-inventories.md) | S | `[x]` |
| **3** | [`e2e-0-3-xml-comments.md`](./e2e-0-3-xml-comments.md) | S | `[x]` |
| **4** | [`e2e-0-4-delete-grammar.md`](./e2e-0-4-delete-grammar.md) | S | `[x]` |
| **5** | [`e2e-0-5-reserved-quantifier-navs.md`](./e2e-0-5-reserved-quantifier-navs.md) | S | `[x]` |
| **G** | [`e2e-0-gate.md`](./e2e-0-gate.md) | S | `[x]` |
