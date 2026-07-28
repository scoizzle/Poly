# Micro-Task: DACR.P5 - Evolution Target Resolution Unification

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Medium
Status: [~] In Progress
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

- [ ] Duplicated target-resolution scans are removed from migrated handlers.
- [ ] Ambiguous or missing targets fail closed with explicit diagnostics.
- [ ] Nullable analysis signatures are removed for migrated semantic handlers.

## Verification

- [x] Build green.
- [x] Evolution tests green.
- [x] New tests for ambiguity and missing-target fail-closed behavior.

## Progress Notes

- [x] Added `MutationTargetIndexMetadata` to analysis metadata contracts.
- [x] `RuntimeContractAnalyzer` now publishes mutation-target indexes for domain types, entities, relationships, stages, actions, and policies.
- [x] Added coverage in `RuntimeContractMetadataTests` to assert mutation target index production.
- [x] Added shared target resolution helpers (`ResolveStage`, `ResolveAction`) in `DomainMutationContext` with explicit `ResolveStatus` mapping.
- [x] Migrated selected handlers (`RemovePolicyFromStageChange`, `RemovePolicyFromActionChange`) to use shared resolution and fail closed on ambiguity.
- [x] `DomainEvolution.Apply` now resolves `MutationTargetIndexMetadata` from analysis and injects it into `DomainMutationContext` for semantic target resolution.
- [x] Added ambiguity fail-closed regression coverage in `EvolutionRollbackTests` for policy-on-action mutation routes.
- [x] Migrated additional handlers (`AddPolicyToActionChange`, `AddActionToStageChange`, `RemoveActionFromStageChange`, `RemoveStageSubscriptionChange`) to shared resolution status checks.
- [x] Added live-context fallback for same-batch additions so index-backed resolution stays compatible with multi-step mutation batches.
- [ ] Next: migrate remaining prioritized handlers that still rely on generic direct helper scans and tighten diagnostics mapping consistency.
