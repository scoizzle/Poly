# pack-2-3 — SqlServer as IDomainPack

**Difficulty:** S  
**Status:** `[x]`  
**Wave:** D · **Prereq:** pack-2-1 `[x]`  

## Objective

`Poly.Packs.SqlServer` is an `IDomainPack`. Maps + identifier convention unchanged.

## Exact steps

1. Failing test on existing `SqlServerPackTests` (or new): `AddPack_SqlServer_AppliesTypeMaps`.
2. `SqlServerPack : IDomainPack` (`Id` = `"sqlserver"`). Apply type maps + existing identifier convention.
3. Keep or wrap `SqlServerDefaults`. Update only this pack + its tests.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*SqlServerPack*"
```

- [ ] Behavior identical  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `src/Poly.Packs.SqlServer/**` | `src/Poly.Packs.Sqlite/**` |
| `Poly.Tests/DomainModeling/Lowering/SqlServerPackTests.cs` | `DslCompiler.cs` |

## Status

**Status:** `[x]`  
**Claimed by: opencode (pack-2-3-sqlserver)**
