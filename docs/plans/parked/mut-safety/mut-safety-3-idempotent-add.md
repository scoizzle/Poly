# mut-safety-3 — Idempotent structural add

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 1  

## Objective

Re-adding an existing structural element returns **Success: true** with **`was_noop: true`** (in `Data` or response field), does not rollback, does not duplicate.

## Exact steps

1. For each operation the **current catalog** exposes for structural add (either micro-tools **or** unified `add` kinds), implement existence check **before** or **inside** evolve helper:

| Kind / tool | Duplicate means |
|-------------|-----------------|
| entity | same entity name |
| property | same name on entity |
| stage | same stage name on entity |
| action | same action name on entity |
| stage_action | same action on stage |
| relationship | same relationship name |
| policy | same policy name on entity (entity-level) |

2. Response: include `was_noop: true` when no DomainChange applied. Prefer extend `DomainToolResponse.Data` anonymous object consistently.

3. Tests:

| Test | Expect |
|------|--------|
| `AddEntity_Twice_SecondIsNoop` | both Success; second was_noop true; one entity |
| `AddProperty_Twice_SecondIsNoop` | same |

4. Do **not** make removes idempotent unless trivial (out of scope).

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Noop flag present and tested  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/**` tool + store helpers | Core evolution semantics beyond noop checks |

## Status

**Status:** Not Started  
