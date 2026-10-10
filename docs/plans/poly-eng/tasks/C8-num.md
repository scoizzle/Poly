# TASK C8-num - Number C# type from constraints
Status: planned. Branch slice/c8-num-numeric-types. Lane B. Review 2. Implement mill: Grok (review on OpenCode).
## Scope
Finding D8 `orders/Order.cs.golden:24`. Depends on C8-clean (exporter cleanups land first so goldens are stable before every numeric signature rewrites). Scot 2026-10-10 15:05 CT: a DSL `Number`'s C# type is decided by its applied constraints (integral means `long`; fractional bounds or precision mean `decimal`).
Today `DomainTypeMapping.ToClrTypeName` (`Poly/DomainModeling/Meaning/DomainTypeMapping.cs:14`) maps `"Number"` / `"Int"` / `"Int64"` all to `"long"`. Probe domains use `Number` for money (`NightlyRate`, `Balance`, `DayRate`, `Discount`, `Total`, `ListPrice`) — fractional amounts silently truncate. A separate `Decimal` type exists (`:17`) and is unused by these domains. The mapping must read the property's applied constraints (range with a fractional bound, or a precision/scale constraint), not only the type name.
## Files
- `Poly/DomainModeling/Meaning/DomainTypeMapping.cs:14` (and any analysis/lowering that passes applied constraints into the CLR mapping)
- Exporter/tests/goldens that emit numeric CLR types (entity factories, action params, DbContext columns, DTOs)
- `Poly.Tests/DomainModeling/Compile/EmitGoldenTests.cs`, `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs`
- Goldens: regenerate `Poly.Tests/DomainModeling/Compile/EmitGolden/**` (none hand-edited)
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `Number` with integral constraints / no fractional bound (D8) | `long` | `long` | D8 `orders/Order.cs.golden:24` (integral properties stay `long`) |
| `Number` with fractional bounds or precision (money: `NightlyRate`, `Balance`, `Discount`, `Total`, `ListPrice`) | `long` (truncates) | `decimal` | D8 `orders/Order.cs.golden:24` and the money properties in hotel/crm goldens |
| Explicit DSL `Decimal` | `decimal` | unchanged | existing `DomainTypeMapping` `"Decimal"` arm |
## Done when
- Integral `Number` prints as `long`; `Number` with fractional bounds or precision prints as `decimal`; money properties in the sample domains are `decimal`.
- D8's cited golden shows the constraint-driven type. Goldens regenerated, not hand-edited. Full suite green.
## SHIP if / NOT SHIP if
SHIP if D8's goldens show `decimal` for fractional/money and `long` for integral.

NOT SHIP if every `Number` is one CLR type regardless of constraints, or if money still prints as `long`.
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
The CLR mapping for `Number` reads applied constraints and returns `long` or `decimal`; Scot can open that mapping cold.
## Needs Scot
none
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | grok/grok-4.6 | c0bd182b | planned | D8 |
