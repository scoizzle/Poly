# pack-2-4 — MySql as IDomainPack

**Difficulty:** S  
**Status:** `[x]`  
**Wave:** D · **Prereq:** pack-2-1 `[x]`  

## Objective

`Poly.Packs.MySql` is an `IDomainPack`.

## Exact steps

1. Test: `AddPack_MySql_AppliesTypeMaps` (same as `MySqlDefaults`).
2. `MySqlPack : IDomainPack` (`Id` = `"mysql"`).
3. Only this pack project + tests if any.

## Verification

```bash
dotnet build src/Poly.Packs.MySql/Poly.Packs.MySql.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*MySql*"
```

- [x] Pack applies  
- [x] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `src/Poly.Packs.MySql/**` | other packs / compiler |

## Status

**Status:** Claimed by: opencode (pack-2-4-mysql)  
