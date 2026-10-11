# TASK C8-web - Web API and DbContext emit
Status: planned. Branch slice/c8-web-api. Lane B. Review 2. Implement mill: Grok (review on OpenCode).
## Scope
Findings D3, D4, D5+G13, D6, G14, D16, D17. Depends on C8-num (crm-dslcompiler goldens include Number types). DslCompiler files only; no DomainToCSharpExporter overlap.
D3 `crm-dslcompiler/Program.cs.golden:664` (also 844, 1024; `…/tickets/{ticketId}/close` at 1438 & 1474): duplicate `app.MapPost` registrations for stage-scoped actions (`lose`, `log`, `addline` each 3× with identical routes; `close` 2×). De-duplicate endpoints by (route, verb) before emitting; keep one route per action name per entity (`MinimalApiGenerator.cs`, stage-scoped action listed once per declaring stage — comment at ~line 53).
D4 `crm-dslcompiler/Program.cs.golden:127` (also 165, 192, 219): child-by-id GET endpoints ignore `{activityId}`/`{campaignMemberId}`/`{ticketId}`/`{lineId}` and return `parent.X.FirstOrDefault()` — always the first child. Look up the child by the captured id (`parent.X.FirstOrDefault(x => x.Key == childId)`) and return 404 on miss (`MinimalApiGenerator.cs:571`).
D5+G13 `crm-dslcompiler/Program.cs.golden:88` and `CrmDbContext.cs.golden:30,113,130`: naive `Pluralize = name + "s"` yields `opportunitys`/`activitys` in routes, `DbSet<Opportunity> Opportunitys`, and table names `Opportunitys`/`Activitys`. English pluralization (`Opportunities`, `Activities`) in `MinimalApiGenerator.cs:168`, `DbContextGenerator.cs:301`, `HttpFileGenerator.cs:172`.
D6 `crm-dslcompiler/CrmDbContext.cs.golden:96`: enum property mapped as `HasColumnType("Industry")` — the CLR enum name leaks into SQL DDL. Map enums to TEXT (string) or INTEGER with a converter (`DbContextGenerator.cs`).
G14 `crm-dslcompiler/Program.cs.golden:36`: every failed `DomainResult` becomes HTTP 409 Conflict (range, required, stage). 400 for constraint/stage failures; 409 for unique/conflict only (`MinimalApiGenerator.cs`).
D16 `crm-dslcompiler/Program.cs.golden:1567`: action DTO properties keep DSL lowercase param names (`PlaceHqDto.city`, `OpenDealDto.amount`, `ConvertLeadDto.leadId`) while entity DTOs are PascalCase. Pascalize action DTO property names (`MinimalApiGenerator.cs:742` uses `param.Name` raw).
D17 `crm-dslcompiler/Program.cs.golden:56`: `string.Concat("/api/products/", productResult.Value.Sku.ToString())` calls `.ToString()` on an already-string value. Drop it (`MinimalApiGenerator.cs:453`).
## Files
- `src/Poly.DslCompiler/MinimalApiGenerator.cs` (D3 de-dupe; D4 `:571`; D5+G13 `:168`; G14 status codes; D16 `:742`; D17 `:453`)
- `src/Poly.DslCompiler/DbContextGenerator.cs` (D5+G13 `:301`; D6 enum column type)
- `src/Poly.DslCompiler/HttpFileGenerator.cs` (D5+G13 `:172`)
- `Poly.Tests/DomainModeling/Lowering/MinimalApiGeneratorTests.cs`, `Poly.Tests/DomainModeling/Lowering/DbContextGeneratorTests.cs`, `Poly.Tests/DomainModeling/Compile/EmitGoldenTests.cs`
- Goldens: regenerate `Poly.Tests/DomainModeling/Compile/EmitGolden/crm-dslcompiler/**` (none hand-edited)
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Stage-scoped `lose`/`log`/`addline` (D3) | `MapPost` emitted once per declaring stage (3× identical routes) | one route per action name per entity | D3 `crm-dslcompiler/Program.cs.golden:664` |
| GET child by `{activityId}` (D4) | `parent.X.FirstOrDefault()` ignores the id | lookup by captured id; 404 on miss | D4 `crm-dslcompiler/Program.cs.golden:127` |
| Opportunity / Activity names (D5+G13) | `Opportunitys` / `activitys` in DbSet, table, routes, demo.http | `Opportunities` / `Activities` | D5+G13 `CrmDbContext.cs.golden:30`; `Program.cs.golden:88` |
| Enum column `Industry` (D6) | `HasColumnType("Industry")` | TEXT or INTEGER with a converter | D6 `CrmDbContext.cs.golden:96` |
| Failed `DomainResult` from an action (G14) | HTTP 409 for range, required, stage | 400 for constraint/stage; 409 for unique/conflict only | G14 `crm-dslcompiler/Program.cs.golden:36` |
| Action DTO `PlaceHqDto.city` (D16) | DSL lowercase param names | PascalCase like entity DTOs (`City`) | D16 `crm-dslcompiler/Program.cs.golden:1567` |
| `Sku.ToString()` in location URL (D17) | `.ToString()` on an already-string value | drop `.ToString()` | D17 `crm-dslcompiler/Program.cs.golden:56` |
## Done when
- Each cited golden no longer shows its finding.
- One route per action; child GET by id; `Opportunities`/`Activities`; enum SQL type is TEXT or INTEGER; status codes split; action DTO names PascalCase; no string `.ToString()`.
- Goldens regenerated, not hand-edited. Full suite green.
## SHIP if / NOT SHIP if
SHIP if crm-dslcompiler goldens match and each finding id in this slice is gone from its cited golden.

NOT SHIP if duplicate routes, `Opportunitys`, or id-ignoring child GETs remain, or any listed finding is deferred.
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/MinimalApiGeneratorTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DbContextGeneratorTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
Pluralize, route de-dupe, child-id lookup, enum column type, status-code split, and DTO names are local generator edits; Scot can open those spots cold.
## Needs Scot
none
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | grok/grok-4.6 | c0bd182b | planned | D3 D4 D5 G13 D6 G14 D16 D17 |
