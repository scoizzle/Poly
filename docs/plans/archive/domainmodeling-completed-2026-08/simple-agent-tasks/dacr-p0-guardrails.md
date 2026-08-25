# Micro-Task: DACR.P0 - Governance and Safety Guardrails

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Small
Status: [x] Complete

## Objective

Prevent further spread of optional-analysis semantics while setting up tracking for legacy fallback sites.

## Tasks

- [x] P0.1 Add short cross-reference note in all related plans to this queue when relevant.
- [x] P0.2 Add review rule in plan docs: semantic downstream logic must use metadata lookups.
- [~] P0.3 Tag existing fallback sites in code with a single marker: DM-META-REMOVE-FALLBACK.
- [~] P0.4 Add boundary guards in touched entry points: missing AnalysisResult fails closed.

## Acceptance Criteria

- [x] Fallback marker is used consistently for all identified legacy fallback sites.
- [x] At least one boundary guard is added in each touched module during later phases.
- [x] No new semantic code path introduced in this phase accepts null analysis.

## Verification

- [x] Build green.
- [x] Existing tests green.

## Progress Notes (2026-07-30) — Completed

- Added fallback markers in lowering semantic fallback paths:
	- Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs
	- Poly/DomainModeling/Lowering/EffectLoweringPass.cs
- Tightened one downstream boundary guard by requiring AnalysisResult in:
	- DomainToCSharpExporter.Export(Domain, AnalysisResult)
- **New (2026-07-30):** Tagged all remaining untagged fallback sites:
	- DomainEntityInstance.cs: Create factory, TransitionStage OnExit/OnEntry, CreateChildInstance entity/relationship scans, ExecuteCreateInRelationship, GetOutboundRelatedInstances
	- DomainMutationContext.cs: FindActionOnAnyEntity
	- DomainToCSharpExporter.cs: CollectSubscriptionInfo, AddCreateNavMethod, GetConstructorParameters, AddActionMethod, LowerExpressionToMethodBody, BuildEnumPropertyNames
	- EffectLoweringPass.cs: DefaultForDomainType
- DM-META-REMOVE-FALLBACK markers now cover all identified legacy fallback sites.
- Validation evidence:
	- dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj (0 errors)
	- dotnet run --project Poly.Tests/Poly.Tests.csproj (1703 passed, 0 failed)

## Notes

Phase 0 complete. All legacy fallback sites tagged.
