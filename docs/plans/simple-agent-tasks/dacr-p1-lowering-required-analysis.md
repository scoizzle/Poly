# Micro-Task: DACR.P1 - Lowering Requires AnalysisResult

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Medium
Status: [ ] Not Started
Prereq: DACR.P0 complete

## Objective

Complete lowering semantic lookup migration and remove nullable analysis behavior for lowering semantic routes.

## Tasks

- [ ] P1.1 Implement StageLookupMetadata and consume it in EffectLoweringPass stage transition lookup.
- [ ] P1.2 Ensure create-in relationship target resolution uses resolved metadata path and fails closed when missing.
- [ ] P1.3 Route enum, entity, and relationship semantic lookups through shared helper extensions on AnalysisResult.
- [ ] P1.4 Remove fallback rescans in touched lowering semantic methods.

## Primary Files

- Poly/DomainModeling/Lowering/EffectLoweringPass.cs
- Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs
- Poly/DomainModeling/Analysis/EntityStructureMetadata.cs
- Poly/DomainModeling/Analysis/* pass files for new metadata production

## Acceptance Criteria

- [ ] Lowering semantic methods do not rely on domain rescans for migrated routes.
- [ ] Missing required metadata produces explicit fail-closed error.
- [ ] Public lowering semantic APIs in scope require AnalysisResult.

## Verification

- [ ] Build green.
- [ ] Lowering-related regression tests green.
- [ ] New tests for StageLookupMetadata presence and shape.
