# Micro-Task: DACR.P4 - Runtime Static and Dynamic Split

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Large
Status: [ ] Not Started
Prereq: DACR.P1 complete

## Objective

Keep runtime dynamic state handling local while moving static semantic contracts to metadata-backed resolution.

## Tasks

- [ ] P4.1 Add ActionResolutionMetadata for action and guard resolution by stage.
- [ ] P4.2 Add RelationshipContractMetadata for direction and cardinality contracts.
- [ ] P4.3 Add SubscriptionDispatchPlanMetadata for static subscription matching contract.
- [ ] P4.4 Refactor DomainEntityInstance and DomainInstanceStore to consume these metadata contracts.

## Primary Files

- Poly/DomainModeling/DomainEntityInstance.cs
- Poly/DomainModeling/DomainInstanceStore.cs
- Poly/DomainModeling/Analysis/* for metadata production

## Acceptance Criteria

- [ ] Runtime semantic dispatch does not re-derive static contracts from tree scans in migrated routes.
- [ ] Runtime still owns live instance links and stage values.
- [ ] Missing analysis or required metadata fails closed.

## Verification

- [ ] Build green.
- [ ] Runtime action and subscription tests green.
- [ ] New tests for fail-closed behavior and metadata contract usage.
