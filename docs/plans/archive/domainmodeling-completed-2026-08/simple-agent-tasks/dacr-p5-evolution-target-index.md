# Micro-Task: DACR.P5 - Evolution Target Resolution Unification

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Medium
Status: [x] Complete
Prereq: DACR.P1 complete

## Objective

Centralize mutation target resolution for evolution handlers and remove duplicated semantic name scans.

## Tasks

- [x] P5.1 Add MutationTargetIndexMetadata with entity, stage, action, policy, and relationship indexes.
- [x] P5.2 Add shared resolution helpers and diagnostics mapping.
- [~] P5.3 Refactor DomainMutationContext and selected DomainChange handlers to use unified resolution.
- [x] P5.4 Require AnalysisResult for mutation-target semantic resolution.

## Primary Files

- Poly/DomainModeling/Evolution/DomainMutationContext.cs
- Poly/DomainModeling/Evolution/DomainChange.cs
- Poly/DomainModeling/Analysis/* for metadata production

## Acceptance Criteria

- [x] Duplicated target-resolution scans are removed from migrated handlers.
- [x] Ambiguous or missing targets fail closed with explicit diagnostics.
- [x] Nullable analysis signatures are removed for migrated semantic handlers (handlers use context with MutationTargetIndexMetadata).

## Verification

- [x] Build green.
- [x] Evolution tests green.
- [x] New tests for ambiguity and missing-target fail-closed behavior.

## Progress Notes (2026-07-30) — Completed

- [x] Added `MutationTargetIndexMetadata` to analysis metadata contracts.
- [x] `RuntimeContractAnalyzer` now publishes mutation-target indexes for domain types, entities, relationships, stages, actions, and policies.
- [x] Added coverage in `RuntimeContractMetadataTests` to assert mutation target index production.
- [x] Added shared target resolution helpers (`ResolveStage`, `ResolveAction`) in `DomainMutationContext` with explicit `ResolveStatus` mapping.
- [x] Migrated selected handlers (`RemovePolicyFromStageChange`, `RemovePolicyFromActionChange`) to use shared resolution and fail closed on ambiguity.
- [x] `DomainEvolution.Apply` now resolves `MutationTargetIndexMetadata` from analysis and injects it into `DomainMutationContext` for semantic target resolution.
- [x] Added ambiguity fail-closed regression coverage in `EvolutionRollbackTests` for policy-on-action mutation routes.
- [x] Migrated additional handlers (`AddPolicyToActionChange`, `AddActionToStageChange`, `RemoveActionFromStageChange`, `RemoveStageSubscriptionChange`) to shared resolution status checks.
- [x] Added live-context fallback for same-batch additions so index-backed resolution stays compatible with multi-step mutation batches.
- **[x] New (2026-07-30):** All remaining stage/action-targeting handlers in DomainChange.cs now use context.ResolveStage/ResolveAction or context.UpdateStage/UpdateAction which internally perform metadata-backed resolution:
  - AddOnEntryEffectToStageChange, AddOnExitEffectToStageChange, AddPolicyToStageChange
  - RemoveOnEntryEffectFromStageChange, RemoveOnExitEffectFromStageChange
  - AddEffectToActionChange, RemoveEffectFromActionChange, AddParameterToActionChange, RemoveParameterFromActionChange
  - SetActionResultChange
- FindActionOnAnyEntity tagged with DM-META-REMOVE-FALLBACK marker.
- Validation evidence:
  - dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj (0 errors)
  - dotnet run --project Poly.Tests/Poly.Tests.csproj (1703 passed, 0 failed)
