# pack-2-2 — Sqlite as IDomainPack

**Difficulty:** S  
**Status:** `[x]`  
**Wave:** D · **Prereq:** pack-2-1 `[x]`  

## Objective

`Poly.Packs.Sqlite` is an `IDomainPack`. Existing type maps unchanged.

## Exact steps

1. Failing test: `SqlitePack_Id_IsSqlite` / `AddPack_Sqlite_OverridesNumberColumnType` (same maps as `SqliteDefaults.ApplyTypeMaps`).
2. `[NEW]` `SqlitePack : IDomainPack` (`Id` = `"sqlite"`). `Apply` calls existing `ApplyTypeMaps`.
3. Keep `SqliteDefaults` as wrappers that `AddPack(new SqlitePack())` **or** delete if all call sites move. Grep `AddSqliteDefaults` / `ApplyTypeMaps` and update **only Sqlite project + its tests**.
4. Do not touch SqlServer/MySql/DslCompiler.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*SqlitePack*"
```

- [x] Maps identical to today  
- [x] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `src/Poly.Packs.Sqlite/**` | `src/Poly.Packs.SqlServer/**` |
| `Poly.Tests/DomainModeling/Lowering/SqlitePackTests.cs` | `DslCompiler.cs` |

## Status

**Status:** `[x]`  
**Claimed by: opencode (pack-2-2-sqlite)**
