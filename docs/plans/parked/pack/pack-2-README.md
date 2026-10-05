# pack-2 — Pack surface (`IDomainPack`)

**Parent:** [`../pack-host-2026-08-13.md`](../pack-host-2026-08-13.md) phase 2  
**Fleet:** [`pack-README.md`](./pack-README.md)  
**Gate:** [`pack-2-gate.md`](./pack-2-gate.md)  
**Prereq:** pack-1-gate `[x]`

**Status:** `[x]`

## Objective

`IDomainPack.Apply` is how a pack joins parse, print, and analysis. Sqlite/SqlServer/MySql re-home. MCP and DslCompiler share one `PackSet`.

## Task order

| ID | File | Size | Wave | Status |
|----|------|------|------|--------|
| **1** | [`pack-2-1-idomainpack.md`](./pack-2-1-idomainpack.md) | M | C | `[x]` |
| **2** | [`pack-2-2-sqlite.md`](./pack-2-2-sqlite.md) | S | D | `[x]` |
| **3** | [`pack-2-3-sqlserver.md`](./pack-2-3-sqlserver.md) | S | D | `[x]` |
| **4** | [`pack-2-4-mysql.md`](./pack-2-4-mysql.md) | S | D | `[x]` |
| **5** | [`pack-2-5-compiler.md`](./pack-2-5-compiler.md) | M | E | `[x]` |
| **6** | [`pack-2-6-mcp.md`](./pack-2-6-mcp.md) | M | E | `[x]` |
| **G** | [`pack-2-gate.md`](./pack-2-gate.md) | S | — | `[x]` |
