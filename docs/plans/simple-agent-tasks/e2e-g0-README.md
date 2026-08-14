# e2e-g0 — Full-solution probe gate

**Parent:** parent L9 · fleet P0-0  
**Fleet coordinator:** [`e2e-README.md`](./e2e-README.md)  
**Wave:** 1 · **Required before:** e2e-3, e2e-4  
**Gate:** [`e2e-g0-gate.md`](./e2e-g0-gate.md)

**Status:** `[ ]`

## Objective

`scripts/run-probe.sh` compiles **entities + Program.cs + DbContext** and fails on warnings. Today `probe-check` returns 0 on warnings (`scripts/probe-check/Program.cs` last line: `return errors == 0 ? 0 : 1`).

Do **not** invent a second runner.

## Task order

| ID | File | Size | Status |
|----|------|------|--------|
| **1** | [`e2e-g0-1-warnings-fail.md`](./e2e-g0-1-warnings-fail.md) | S | `[ ]` |
| **2** | [`e2e-g0-2-full-solution.md`](./e2e-g0-2-full-solution.md) | M | `[ ]` |
| **3** | [`e2e-g0-3-fixtures.md`](./e2e-g0-3-fixtures.md) | S | `[ ]` |
| **G** | [`e2e-g0-gate.md`](./e2e-g0-gate.md) | S | `[ ]` |
