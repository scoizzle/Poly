# AMU-W3.1 — DomainToCSharpExporter residual metadata lookups

**Wave:** 3  
**Difficulty:** M  
**Status:** `[x]` — DONE 2026-08-06
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

- [x] Export regression tests green (DomainToCSharpExporterTests, DbContextGeneratorTests, MinimalApiGeneratorTests, DomainToCSharpExportTests green)
- [x] No analysis-null soft dual path reintroduced for required paths (required paths are catalog-first + fail-closed via TryResolveEnumType / GetTypeLookup)
- [x] Build green; full suite 1843/1843

## Implementation notes

`DomainToCSharpExporter.cs`:
- **Deleted dead code:** `DefaultValueForProp` and `DefaultValueForTypeRef` — zero callers repo-wide (verified via grep; only docs mention them). Both contained residual `domain.Types.OfType<EnumType>()` scans. Removed entirely (no consumer = no bag).
- **`MapDomainTypeRef`** — added optional `INodeMetadataProvider? analysis = null`; enum tree-scan now runs only for null-analysis residuals. With analysis present the catalog is the single source of truth and the default `NamedTypeReference(typeName)` case covers enums (identical output — the enum branch only short-circuits the same node).
- **Threaded metadata/analysis at live call sites:** `BuildTypeDefsForEntity` (prop refs, line ~120), CreateNav param refs (~677), `AddActionMethod` result/param refs (~1007/1016), `BuildActionBodyWithGuards` (new optional `analysis` param, threaded from `AddActionMethod`) result refs (~1055).
- **Line-158 enum default block:** `domain.Types.OfType<EnumType>().ToDictionary(...)` → `TryResolveEnumType(domain, metadata, ...)` (catalog-first, fail-closed when analysis present).
- Already catalog-first (unchanged): `TryResolveEnumType`, `BuildEnumPropertyNames`, `ResolveRelationship`, `CollectSubscriptionInfo` (F5 fail-closed), `GetConstructorParameters` (EntityStructure bag, throws when absent).

- **Edit:** `DomainToCSharpExporter.cs` + export tests  
- **Do not edit:** EffectLoweringPass, MCP  

## Status

**Status:** Done — 2026-08-06 (see Implementation notes)  
