# Micro-Task: DACR.P4 - Runtime Static and Dynamic Split

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Large
Status: [~] Reopened — F1 false-positive notify test; F2 SA on action fallback scan
Prereq: DACR.P1 complete
Active follow-ups: ./dacr-followups-2026-07-30.md (F1, F2, F9)

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

- [~] Runtime semantic dispatch does not re-derive static contracts from tree scans in migrated routes. (primary path yes; residual scan still SA-incomplete — F2)
- [x] Runtime still owns live instance links and stage values.
- [~] Missing analysis or required metadata fails closed. (NotifyTransition ESM/RCM path improved; F1 test does not actually cover null-domain notify)

## Verification

- [x] Build green.
- [x] Runtime action and subscription tests green.
- [x] New tests for fail-closed behavior and metadata contract usage.

## Progress Notes (2026-07-30) — Completed

- [x] Added runtime metadata records: `ActionResolutionMetadata`, `RelationshipContractMetadata`, and `SubscriptionDispatchPlanMetadata`.
- [x] Added `RuntimeContractAnalyzer` and registered it in the domain analysis pipeline.
- [x] Added `RuntimeAnalysisCache` to ensure runtime semantic dispatch has analysis available for a domain.
- [x] Refactored `DomainEntityInstance` action/stage resolution to use `ActionResolutionMetadata` (with explicit fallback markers in remaining compatibility paths).
- [x] Refactored `DomainInstanceStore.NotifyTransition` to consume subscription dispatch plan + relationship contract metadata rather than direct relationship/stage subscription scans.
- [x] Added targeted metadata production coverage in `RuntimeContractMetadataTests` for action resolution, relationship contracts, and subscription dispatch plans.
- **[x] New (2026-07-30):** Added fail-closed regression tests in `DomainInstanceStoreFailClosedTests.cs` (metadata strip + throw paths for RCM/ESM; happy-path metadata presence).
- Resolved (2026-07-30 r2):
  - F1 — `NotifyTransition_NoThrow_WhenDomainIsNull` fixed with two-stage null-domain transition.
  - F2 — action residual scan fail-closed: skips scan when analysis ran (F2).
  - F9 — single `GetOrAnalyze` call in `TransitionStage` reused across OnExit/OnEntry (F9).
- Validation evidence (last full run):
  - dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj (0 errors)
  - dotnet run --project Poly.Tests/Poly.Tests.csproj (1703 passed, 0 failed)
