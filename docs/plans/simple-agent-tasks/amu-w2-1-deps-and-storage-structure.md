# AMU-W2.1 — Dependencies honesty + Storage prefers EntityStructure

**Wave:** 2  
**Difficulty:** M  
**Status:** `[x]` — DONE 2026-08-06
**Prereq:** W1 product ACs  

## Objective

1. Declare real `Dependencies` for passes that consume topology / entity structure / catalog (no silent order reliance).  
2. `StorageAnalyzer` / StoragePass path prefer `EntityStructureMetadata` for unique key / soft-delete / stage-related facts when present instead of re-deriving solely from property scans.

## Required reading

- Pass `Dependencies` arrays under `Analysis/`  
- `StorageAnalyzer.cs`, `StoragePass.cs`, `EntityStructureMetadata.cs`  
- OwnershipAggregate / topology consumers  

## Exact steps

1. Audit empty `Dependencies => []` publishers and their consumers.  
2. Add deps where missing (e.g. consumers of EffectTopology, EntityStructure).  
3. Storage: if `EntityStructureMetadata` present, use it for overlapping fields; scan only for storage-specific residuals.  
4. Fail closed if StoragePass requires hierarchy and bags missing (align TransportPass posture if not already).  
5. Tests for storage mapping under full domain analyze.

## Verification

- [x] Pipeline order stable; no circular deps (PassDependencyDeclarationTests green)
- [x] Storage goldens / DAU-style storage tests green (InfrastructureAnalyzerTests, SqlitePackTests, SqlServerPackTests green)
- [x] Build green; full suite 1843/1843

## Implementation notes

**Deps audit (steps 1–2):**
- `ConstraintPropagationAnalyzer` `[]` — honest: pure effect-tree scan, publishes only; doc comment already says "no upstream analysis bags required". No change.
- `ContractIntegrationAnalyzer` `[]` — honest: lint-only, reads tree (ImportedContracts/ContractBindings) only. No change.
- `EffectTopologyPass` `[]` — honest: pure domain-tree scan; consumers (CrossReferencePass, OwnershipAggregatePass, StoragePass, TransportPass) already declare it. No change.
- `StructuralDomainAnalyzer` / `SemanticDomainAnalyzer` `[]` — pipeline roots. No change.
- `StoragePass` — **added `EntityStructureAnalyzer.Id`** to Dependencies (consumes EntityStructure facts; previously only via priorAnalysis bypass, so AnalyzerBuilder ordering was accidental, not declared).

**Storage prefers EntityStructureMetadata (step 3):**
- `StorageAnalyzer` now accepts optional `AnalysisContext` and resolves `EntityStructureMetadata` **context-first** (full-pipeline path — EntityStructureAnalyzer runs before StoragePass), falling back to `_analysis` (standalone/codegen path). Same context-first treatment for `DomainTypeLookupMetadata` (entity list) and parent-entity FK meta in `BuildForeignKeys`.
- Before: through the AnalyzerBuilder pipeline (`new StoragePass()` — no analysis), the EntityStructure bag was published into context but StorageAnalyzer ignored it and re-scanned properties/constraints. Now the bag is the primary source; scans remain only as fallback.

**Fail closed (step 4):** StoragePass already fails loud when topology/aggregate missing ("StoragePass.MissingDependency" error, mirror of TransportPass Issue 17 posture). TransportPass already aligned. No change needed.

**Tests (step 5):** `Analyze_StorageMapping_PrefersEntityStructureBagForKeyDeleteAndStages` — full pipeline analyze (no priorAnalysis) on an entity with `Sku: Text unique`, `IsDeleted: Boolean default(false)`, Draft/Active stages; asserts EntityStructure bag present AND storage surface reflects it (`KeyName == "sku"`, HasSoftDelete, HasStages, `StageEnumTypeName == "ItemStage"`).  

## File ownership

- **Edit:** Storage* + Dependency lines on affected analyzers; storage tests  
- **Do not edit:** MCP, EffectAnalyzer (unless deps line only)  

## Status

**Status:** Done — 2026-08-06 (see Implementation notes)  
