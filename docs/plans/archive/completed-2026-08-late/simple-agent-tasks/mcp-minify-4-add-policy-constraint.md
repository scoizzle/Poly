# mcp-minify-4 — Unified `add` for constraint + policy

**Difficulty:** M  
**Status:** `[x]`  
**Prereq:** task 3 `[x]`, task 1 `[x]`

**Done 2026-08-08:** `add` dispatch extended with `constraint` (entityName/propertyName/type + min/max/pattern via existing `BuildConstraint`) and `policy` (entityName/name/expression — DSL fragment via `DslExpressionFragment.ParseExpressionFragment`, entity-level only). `add_policy` now delegates to the same `EvolveTool.AddPolicyCore` helper (DSL only, no JsonParser) — success message preserved via `with`. **Deleted** `DomainExpressionJsonParser.cs` + `DomainExpressionJsonParserTests.cs` (zero callers). All `add_policy` test callers converted to DSL (McpSmokeTests, DomainSemanticLookupFailClosedTests, OracleToolTests — including relationship-nav DSL `profile City is "Metropolis"`). 4 new tests in `UnifiedAddTests` (constraint Required, policy DSL success, invalid DSL fails with no silent empty policy, JSON-bag expression fails closed). Suite 1929 green.  

## Objective

Extend MCP tool **`add`** with kinds `constraint` and `policy`. Policy expression is **DSL fragment** only (task 1 API). After this task, **stop calling** `DomainExpressionJsonParser` from `add_policy` by routing policies only through `add` **or** rewrite `add_policy` body to call the same helper then delete `add_policy` in task 6.

## Required reading

1. Existing `add_constraint` + `add_policy` in `DomainTools.cs`  
2. Task 1 fragment API  
3. Parent plan §3.3 policy/constraint rows  

## Exact steps

1. Extend `add` dispatch:

### kind `constraint`

Required payload fields (match existing `add_constraint` semantics):

- `entityName`, `propertyName`, `type` where `type` is one of: `Required`, `Unique`, `Range`, `Length`, `Pattern` (same strings as today).  
- Type-specific: `Range`/`Length` need `min`/`max` as appropriate; `Pattern` needs `pattern` string — **copy validation from existing tool**.

### kind `policy`

Required:

- `entityName`, `name`, `expression` (DSL string, e.g. `Age >= 18`)  
- Optional later: scope — **v1 entity-level only** unless existing `add_policy` is entity-only (it is). Keep entity-level only.

Parse expression:

```csharp
var expr = PolyDslParser.ParseExpressionFragment(expression, sessionParserInputsIfAny);
```

Use session parser inputs if the session stores them; else `null`.

2. Wire Evolve to same methods as old tools (`AddConstraint…`, `AddPolicyToEntity`).  

3. Tests in `UnifiedAddTests` (or sibling):

| Test | Expect |
|------|--------|
| `Add_Constraint_Required_Succeeds` | constraint present |
| `Add_Policy_DslFragment_Succeeds` | policy with comparison; **not** JSON |
| `Add_Policy_InvalidDsl_Fails` | Success false, no silent empty policy |
| `Add_Policy_JsonBag_Fails` | payload expression `{"property":"Age"...}` fails parse (fail closed) |

4. If `add_policy` still exists, change it to **delegate** to the same internal helper as `add(kind=policy)` **or** mark obsolete — must **not** call `DomainExpressionJsonParser`. Prefer: leave tool registered until task 6 but body uses fragment API (avoids breaking tests mid-suite). Task 6 deletes it.

5. Grep:

```bash
rg -n "DomainExpressionJsonParser" --glob '*.cs'
```

If only tests for JsonParser remain, delete production class **or** leave for task 6 if still referenced by tests you’ll delete in 6.

## Verification

```bash
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
rg -n "DomainExpressionJsonParser" Poly.Mcp --glob '*.cs'
# Expect: no matches in Poly.Mcp
```

- [ ] `add` supports constraint + policy  
- [ ] Poly.Mcp has zero JsonParser references  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/Tools/*` add path | Full micro-tool deletion (task 6) |
| `Poly.Tests/Mcp/*` | |

## Status

**Status:** Done  
