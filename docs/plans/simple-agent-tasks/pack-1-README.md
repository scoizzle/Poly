# pack-1 — DSL Grammar (TokenWriter + binder + printer)

**Parent:** [`../pack-host-2026-08-13.md`](../pack-host-2026-08-13.md) phase 1  
**Fleet:** [`pack-README.md`](./pack-README.md)  
**Gate:** [`pack-1-gate.md`](./pack-1-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)

**Status:** `[x]` Done 2026-08-13 — gate passed (pr1 clean, build + suite green)

## Objective

Pack-shaped grammar patterns parse and print through `Matcher` + `Printer` + `DslTokenWriter`. Unprintable IR is loud. No `IDomainPack` yet.

## Task order

| ID | File | Size | Wave | Status |
|----|------|------|------|--------|
| **1** | [`pack-1-1-token-writer.md`](./pack-1-1-token-writer.md) | M | A | `[x]` |
| **2** | [`pack-1-2-print-binder.md`](./pack-1-2-print-binder.md) | M | A | `[x]` |
| **3** | [`pack-1-3-dsl-printer.md`](./pack-1-3-dsl-printer.md) | M | B | `[x]` |
| **4** | [`pack-1-4-e1-patterns.md`](./pack-1-4-e1-patterns.md) | M | B | `[x]` |
| **G** | [`pack-1-gate.md`](./pack-1-gate.md) | S | — | `[x]` |
