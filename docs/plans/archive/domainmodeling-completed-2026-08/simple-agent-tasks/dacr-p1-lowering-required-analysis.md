# Micro-Task: DACR.P1 - Lowering Requires AnalysisResult

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Medium
Status: [x] Complete (residual F5 exporter fail-closed wording tracked in follow-ups)
Prereq: DACR.P0 complete
Active follow-ups: ./dacr-followups-2026-07-30.md (F5)

## Objective

Complete lowering semantic lookup migration and remove nullable analysis behavior for lowering semantic routes.

## Tasks

- [ ] P1.1 Implement StageLookupMetadata and consume it in EffectLoweringPass stage transition lookup.
- [ ] P1.2 Ensure create-in relationship target resolution uses resolved metadata path and fails closed when missing.
- [~] P1.3 Route enum, entity, and relationship semantic lookups through shared helper extensions on AnalysisResult.
- [~] P1.4 Remove fallback rescans in touched lowering semantic methods.

## Primary Files

- Poly/DomainModeling/Lowering/EffectLoweringPass.cs
- Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs
- Poly/DomainModeling/Analysis/EntityStructureMetadata.cs
- Poly/DomainModeling/Analysis/* pass files for new metadata production

## Acceptance Criteria

- [x] Lowering semantic methods do not rely on domain rescans for migrated routes.
- [x] Missing required metadata produces explicit fail-closed error.
- [x] Public lowering semantic APIs in scope require AnalysisResult.

## Verification

- [x] Build green.
- [x] Lowering-related regression tests green.
- [x] New tests for StageLookupMetadata presence and shape (StageByName covered by EntityStructureMetadata tests).

## Progress Notes (2026-07-30) — Completed

- Contract tightening applied at export boundary:
	- DomainToCSharpExporter.Export now requires AnalysisResult.
	- DomainProgramProjection gained AnalysisResult overload for downstream callers.
- Fallback elimination work started via explicit tracked markers:
	- Enum/relationship/entity and constructor-order fallback paths in lowering tagged with DM-META-REMOVE-FALLBACK.
- **New (2026-07-30):** Created `DomainSemanticLookupExtensions` — shared helper extension methods on AnalysisResult:
	- TryGetStage(Entity, string, out Stage?) — metadata-backed stage resolution
	- TryResolveAction(Entity, string?, string, out Action?) — metadata-backed action resolution (entity + stage-scoped)
	- GetEffectivePolicies(Entity, string) — aggregated policy lookup via MutationTargetIndexMetadata
	- TryGetRelationship(string, out Relationship?) — metadata-backed relationship resolution
	- GetOutboundRelationships(string) / GetInboundRelationships(string) — via RelationshipContractMetadata
	- TryGetEntity(string, out Entity?) / TryGetEnumType(string, out EnumType?) — via DomainTypeLookupMetadata
- StageLookupMetadata concept covered by existing EntityStructureMetadata.StageByName (consumed in EffectLoweringPass.StageTransition)
- Stage transition and create-in relationship resolution already use metadata-backed paths
- Validation evidence:
	- dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj (0 errors)
	- dotnet run --project Poly.Tests/Poly.Tests.csproj (1703 passed, 0 failed)
