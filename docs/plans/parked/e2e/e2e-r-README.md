# e2e-r — Parameter identity + invoke-arg + type-check

**Parent:** slice R · L3 (revised) · L8  
**Fleet coordinator:** [`e2e-README.md`](./e2e-README.md)  
**Wave:** 2 · **One agent sequential** (hot files)  
**Gate:** [`e2e-r-gate.md`](./e2e-r-gate.md)

**Status:** `[ ]`

## Objective

In-scope action parameters work on the product path. Unknown / missing / mistyped args fail closed.

**L3 lock:** consult `paramEnv` on matching `PropertyAccess`. Do **not** emit `ParameterAccess` from `ParsePrimary`. Parser edit = decimals only (task 6).

## Task order

| ID | File | Size | Status |
|----|------|------|--------|
| **0** | [`e2e-r-0-inventory.md`](./e2e-r-0-inventory.md) | S | `[ ]` |
| **1** | [`e2e-r-1-paramenv-propertyaccess.md`](./e2e-r-1-paramenv-propertyaccess.md) | M | `[ ]` |
| **2** | [`e2e-r-2-invoke-declared-args.md`](./e2e-r-2-invoke-declared-args.md) | M | `[ ]` |
| **3** | [`e2e-r-3-binding-caller-params.md`](./e2e-r-3-binding-caller-params.md) | M | `[ ]` |
| **4** | [`e2e-r-4-unknown-bypass.md`](./e2e-r-4-unknown-bypass.md) | M | `[ ]` |
| **5** | [`e2e-r-5-if-and-keywords.md`](./e2e-r-5-if-and-keywords.md) | M | `[ ]` |
| **6** | [`e2e-r-6-decimal-literals.md`](./e2e-r-6-decimal-literals.md) | S | `[ ]` |
| **7** | [`e2e-r-7-enum-null-defaults.md`](./e2e-r-7-enum-null-defaults.md) | M | `[ ]` |
| **8** | [`e2e-r-8-date-param-arithmetic.md`](./e2e-r-8-date-param-arithmetic.md) | S | `[ ]` |
| **9** | [`e2e-r-9-mcp-json-types.md`](./e2e-r-9-mcp-json-types.md) | M | `[ ]` |
| **G** | [`e2e-r-gate.md`](./e2e-r-gate.md) | S | `[ ]` |
