# pack-2-5 — DslCompiler uses PackSet

**Difficulty:** M  
**Status:** `[x]`  
**Wave:** E · **Prereq:** pack-2-2 `[x]`   (Sqlite as proving pack)  

## Objective

`DbmsPack` is a convenience alias, not the pack model. Compiler takes a `PackSet` / packs and builds inputs via `AddPack`.

## Exact steps

1. Failing test in `DslCompilerCompileOracleTests` or `SqlitePackTests`: compile with `new SqlitePack()` produces the same column types as `--dbms sqlite` does today.
2. `[MODIFY]` `src/Poly.DslCompiler/DslCompiler.cs`:
   - Add overload `Compile(..., PackSet packs)` or `params IDomainPack[]`.
   - `DbmsPack` enum maps to AddPack (Sqlite/SqlServer/generic) so CLI `--dbms` still works.
   - Do not add new enum arms for future packs.
3. Update `Program.cs` only if needed for the alias.
4. Do not move Minimal API to artifact contributors (3c).

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*DslCompiler*"
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*SqlitePack*"
```

- [x] sqlite/sqlserver/generic still compile  
- [x] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `src/Poly.DslCompiler/DslCompiler.cs` | `MinimalApiGenerator.cs` (unless compile-broken) |
| `src/Poly.DslCompiler/Program.cs` | MCP |
| compiler tests | `DomainDslPrinter.cs` |

## Status

**Status:** Claimed by: opencode (pack-2-5-compiler)  
