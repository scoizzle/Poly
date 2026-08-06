# AMU-W3.2 — EffectLoweringPass residual metadata lookups

**Wave:** 3  
**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** W1  
**Parallel OK with:** W3.1  

## Objective

With analysis present, EffectLowering resolves entities/relationships/stages via metadata helpers rather than `_domain.Relationships` / `Types.OfType` / stage rescans where bags exist (e.g. EntityStructure.TryGetStage, catalog).

## Required reading

- `EffectLoweringPass.cs`, `LoweringContext.cs`  
- Stage transition lowering comments in file  
- Effect lowering tests  

## Exact steps

1. Grep residual domain scans under analysis-present branches.  
2. Use existing resolved create-in metadata; extend only if needed.  
3. Analysis-present stage miss stays fail-loud (quality followups).  
4. Tests for create-in / transition lowering with analysis.

## Verification

- [ ] Lowering tests green  
- [ ] Standalone reduced contract unchanged or documented  

## File ownership

- **Edit:** `EffectLoweringPass.cs` (+ helpers in Lowering/) + tests  
- **Do not edit:** DomainToCSharpExporter, MCP  

## Status

**Status:** Not Started  
