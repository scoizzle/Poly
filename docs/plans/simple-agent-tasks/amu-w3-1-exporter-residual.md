# AMU-W3.1 — DomainToCSharpExporter residual metadata lookups

**Wave:** 3  
**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** W1  
**Parallel OK with:** W3.2  

## Objective

When `AnalysisResult` is present, exporter resolves enum/type maps and entity structure via analysis metadata (catalog / DTLM / EntityStructure) instead of repeated `domain.Types.OfType<EnumType>()` where a bag already exists.

## Required reading

- `DomainToCSharpExporter.cs`  
- analysis-consuming-lowering historical notes (archived parent if needed)  
- Export / codegen tests  

## Exact steps

1. Grep exporter for `OfType<EnumType>`, type dictionary rebuilds.  
2. When analysis non-null, use type lookup / catalog / structure.  
3. Fail closed on missing required bags for semantic export paths (existing contract).  
4. Keep property/stage iteration for **emission order** from domain model (printer-like) unless structure bag owns ordering.  

## Verification

- [ ] Export regression tests green  
- [ ] No analysis-null soft dual path reintroduced for required paths  

## File ownership

- **Edit:** `DomainToCSharpExporter.cs` + export tests  
- **Do not edit:** EffectLoweringPass, MCP  

## Status

**Status:** Not Started  
