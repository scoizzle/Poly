# Micro-Task: DACR.P0 - Governance and Safety Guardrails

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Small
Status: [~] In Progress

## Objective

Prevent further spread of optional-analysis semantics while setting up tracking for legacy fallback sites.

## Tasks

- [x] P0.1 Add short cross-reference note in all related plans to this queue when relevant.
- [x] P0.2 Add review rule in plan docs: semantic downstream logic must use metadata lookups.
- [~] P0.3 Tag existing fallback sites in code with a single marker: DM-META-REMOVE-FALLBACK.
- [~] P0.4 Add boundary guards in touched entry points: missing AnalysisResult fails closed.

## Acceptance Criteria

- [ ] Fallback marker is used consistently for all identified legacy fallback sites.
- [ ] At least one boundary guard is added in each touched module during later phases.
- [ ] No new semantic code path introduced in this phase accepts null analysis.

## Verification

- [ ] Build green.
- [ ] Existing tests green.

## Progress Notes (2026-07-28)

- Added fallback markers in lowering semantic fallback paths:
	- Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs
	- Poly/DomainModeling/Lowering/EffectLoweringPass.cs
- Tightened one downstream boundary guard by requiring AnalysisResult in:
	- DomainToCSharpExporter.Export(Domain, AnalysisResult)
- Validation evidence:
	- dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
	- dotnet run --project Poly.Tests/Poly.Tests.csproj (1693 passed)

## Notes

Keep this phase light: only guardrails and tracking, no major refactors.
