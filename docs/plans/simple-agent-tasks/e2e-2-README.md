# e2e-2 — Quantifier policies vs generated actions

**Parent:** slice 2 · L5 · 05-F5  
**Wave:** 4 after e2e-s-3 (exporter free) · after e2e-r (lowering free)

**Status:** Quantifier policies and action-expression quantifiers lower to the same `foreach` in simulate and print. Tests cover policy bodies, action `if` / `assign` / short-circuit `and`, void and entity-returning create initializers, for-each invoke arguments, path-prefix hops whose target contains a quantifier, a temporal date-add with a quantifier child, action-parameter names winning inside a quantifier body, and a `for` whose predicate policy is itself a quantifier. Tasks below that are not that lowering stay `[ ]`.

## Task order

| ID | File | Size | Status |
|----|------|------|--------|
| **0** | [`e2e-2-0-decision.md`](./e2e-2-0-decision.md) | S | `[ ]` |
| **1** | [`e2e-2-1-implement.md`](./e2e-2-1-implement.md) | M | `[ ]` |
| **2** | [`e2e-2-2-guide.md`](./e2e-2-2-guide.md) | S | `[ ]` |
| **G** | [`e2e-2-gate.md`](./e2e-2-gate.md) | S | `[ ]` |
