# e2e-s — Subscription fidelity

**Parent:** slice S · fleet P6  
**Wave:** 3 (runtime + analyzer) · **S-3 waits for wave 4** (exporter)

**Status:** `[ ]`

## Task order

| ID | File | Size | Status |
|----|------|------|--------|
| **1** | [`e2e-s-1-notify-snapshot.md`](./e2e-s-1-notify-snapshot.md) | S | `[ ]` |
| **2** | [`e2e-s-2-multistage-all.md`](./e2e-s-2-multistage-all.md) | M | `[ ]` |
| **3** | [`e2e-s-3-export-order.md`](./e2e-s-3-export-order.md) | S | `[ ]` |
| **4** | [`e2e-s-4-missing-peer-error.md`](./e2e-s-4-missing-peer-error.md) | S | `[ ]` |
| **G** | [`e2e-s-gate.md`](./e2e-s-gate.md) | S | `[ ]` |

S-4 may run in parallel with S-1 (different file). S-3 only after e2e-1-2 exporter slot is free.
