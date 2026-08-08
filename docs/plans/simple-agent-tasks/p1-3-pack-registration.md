# p1-3 — Built-in temporal pack registration

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** tasks 1–2  

## Objective

Product default input set (or explicit `CreateWithTemporalPack`) registers temporal forms so MCP/session DSL and fragment parse get Now/units without ad-hoc test-only registration.

## Exact steps

1. Add registration helper e.g. `TemporalPack.Register(DomainInputBuilder)` or `DomainInputBuilder.CreateWithTemporalPack()` mirroring SQL pack pattern.  
2. Register all temporal `IExpressionPrimaryForm`s + any grammar contributors.  
3. Wire product default **or** document that sessions must opt in — design lock: **built-in pack product default**. Prefer default-on for DomainInputDefaults / MCP session bootstrap.  
4. If default-on breaks a test that used `Now` as property name, fix test domains (rename property).  
5. Test: session/default inputs parse `Now - 1 days` without manual Register in test (or builder CreateWithTemporalPack explicitly tested).

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Pack registration single entry point  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| DomainInputBuilder / Bootstrap / small TemporalPack type | Unrelated packs |

## Status

**Status:** Not Started  
