# pack-2-6 — MCP session shares PackSet for apply + export

**Difficulty:** M  
**Status:** `[x]`  
**Wave:** E · **Prereq:** pack-2-1 `[x]`  

## Objective

`apply_dsl` and `export_dsl` use the **same** session `PackSet` (parser inputs + printer inputs). Sql annotation pack remains the default product set.

## Exact steps

1. Failing test if MCP has test hooks; else a DomainModeling-level test that constructs the same inputs the session will use: parse + print with annotations registered.
2. Session store holds `PackSet` / `DomainInputSet` built once per session.
3. `apply_dsl` → `PolyDslParser(text, session.ParserInputs)`.
4. `export_dsl` → `DomainDslPrinter(session.ParserInputs)` (or equivalent).
5. Do not add a tool to toggle packs unless one already exists. Default = current Sql annotations.
6. Guide unchanged.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*DomainTools*"
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*Mcp*"
```

- [x] apply and export share inputs  
- [x] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/**` session + apply/export | `DslCompiler.cs` |
| MCP tests | `src/Poly.Packs.*` |

## Status

**Status:** `[x]`  
**Claimed by: opencode (pack-2-6-mcp)**
