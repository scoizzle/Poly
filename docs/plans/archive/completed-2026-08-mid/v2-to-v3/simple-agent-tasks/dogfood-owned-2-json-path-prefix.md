# owned-2 — `add_policy` JSON form for path-prefix (owned + nav)

**Suite:** [`dogfood-owned-README.md`](dogfood-owned-README.md)  
**Source finding:** S3-B1 — `add_policy` JSON cannot express owned or relationship field access  
**Difficulty:** Small  
**Status:** `[x]`

## What was done

Extended `DomainExpressionJsonParser.ParseElement` with a `"relationship"` key branch:

## Required Reading

- `Poly/DomainModeling/Lowering/DomainExpressionJsonParser.cs` — full file  
- `Poly/DomainModeling/DomainExpression.cs` — `RelationshipNavigation`, `OwnedAccess`  
- DomainExpression factory: `DomainExpression.RelationshipNav(string relName, DomainExpression inner)`  
- Poly.Mcp/Docs/poly-dsl-guide.md — path-prefix pattern (guide now says shipped)

## Exact Steps

1. Add `"relationship"` key support to `ParseElement`:
   ```json
   {"relationship":"profile","inner":{"property":"City","op":"==","value":"Metropolis"}}
   ```
   Parses to: `RelationshipNavigation("profile", Equal(PropertyAccess("City"), Literal("Metropolis")))`

2. Validation:
   - `"relationship"` must be a non-empty string
   - `"inner"` must be a valid expression (recursively parsed)
   - Fail-closed on missing/invalid fields

3. Test: unit test or MCP smoke test confirming `add_policy` / `simulate_policy` accepts the new JSON shape:
   - `simulate_policy` with path-prefix JSON works (simulate_policy doesn't need store linkage, just lower and eval)
   - `add_policy` with path-prefix JSON accepts the expression

4. Note: The same JSON shape works for both regular navigations and owned, since both use the same path-prefix lowering.

## Out of scope

- Runtime eval with store-linked instances (owned-3)  
- Dot syntax (`profile.City`) — explicitly not supported per guide  
- Richer `exists` / `where` in JSON — those remain DSL-only for now

## Definition of Done

- [x] `simulate_policy` accepts `"relationship"` JSON and evaluates correctly  
- [x] `add_policy` accepts `"relationship"` JSON  
- [x] Fail-closed on missing relationship name or inner expression  
- [x] Automated test  
- [x] Build + targeted tests green  
- [x] owned-README CURRENT → owned-3

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
