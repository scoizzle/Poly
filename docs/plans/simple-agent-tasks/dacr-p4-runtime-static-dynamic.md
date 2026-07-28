# Micro-Task: DACR.P4 - Runtime Static and Dynamic Split

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Large
Status: [~] In Progress
Prereq: DACR.P1 complete

## Objective

Keep runtime dynamic state handling local while moving static semantic contracts to metadata-backed resolution.

## Tasks

- [x] P4.1 Add ActionResolutionMetadata for action and guard resolution by stage.
- [x] P4.2 Add RelationshipContractMetadata for direction and cardinality contracts.
- [x] P4.3 Add SubscriptionDispatchPlanMetadata for static subscription matching contract.
- [x] P4.4 Refactor DomainEntityInstance and DomainInstanceStore to consume these metadata contracts.

## Primary Files

- Poly/DomainModeling/DomainEntityInstance.cs
- Poly/DomainModeling/DomainInstanceStore.cs
- Poly/DomainModeling/Analysis/* for metadata production

## Acceptance Criteria

- [ ] Runtime semantic dispatch does not re-derive static contracts from tree scans in migrated routes.
- [ ] Runtime still owns live instance links and stage values.
- [ ] Missing analysis or required metadata fails closed.

## Verification

- [x] Build green.
- [x] Runtime action and subscription tests green.
- [~] New tests for fail-closed behavior and metadata contract usage.

## Progress Notes

- [x] Added runtime metadata records: `ActionResolutionMetadata`, `RelationshipContractMetadata`, and `SubscriptionDispatchPlanMetadata`.
- [x] Added `RuntimeContractAnalyzer` and registered it in the domain analysis pipeline.
- [x] Added `RuntimeAnalysisCache` to ensure runtime semantic dispatch has analysis available for a domain.
- [x] Refactored `DomainEntityInstance` action/stage resolution to use `ActionResolutionMetadata` (with explicit fallback markers in remaining compatibility paths).
- [x] Refactored `DomainInstanceStore.NotifyTransition` to consume subscription dispatch plan + relationship contract metadata rather than direct relationship/stage subscription scans.
- [x] Added targeted metadata production coverage in `RuntimeContractMetadataTests` for action resolution, relationship contracts, and subscription dispatch plans.
- [ ] Add explicit fail-closed regression tests that simulate unavailable/corrupt runtime metadata paths.
