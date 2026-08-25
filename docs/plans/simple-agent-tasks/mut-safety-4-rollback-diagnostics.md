# mut-safety-4 — Rollback diagnostics on failed evolve

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 1  

## Objective

When `DomainEvolution.Apply` fails (analysis rollback), MCP mutation response includes enough diagnostics that an agent need not re-scan every entity.

## Exact steps

1. Inspect `EvolutionResult` / existing `FailureSummary`.  
2. On MCP evolve failure responses, ensure `Data` (or structured fields) includes at least:

   - `failureSummary` (string; may already exist as Message)  
   - `survivingEntityNames` : string[] **or** map name → stage count  
   - Prefer reusing existing fields if already sufficient — document gap fill only  

3. Cap payload: entity **names** (+ optional counts), not full property dumps.  

4. Test: force a known analysis failure (e.g. invalid evolve if easy; or invalid type name) and assert response contains surviving entity list or failure summary non-empty.

5. Do not change analysis rules.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Failed evolve carries diagnostics  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/**`, optionally thin EvolutionResult exposure if already public | New analyzers |

## Status

**Status:** Not Started  
