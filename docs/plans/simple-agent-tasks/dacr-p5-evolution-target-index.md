# Micro-Task: DACR.P5 - Evolution Target Resolution Unification

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Medium
Status: [ ] Not Started
Prereq: DACR.P1 complete

## Objective

Centralize mutation target resolution for evolution handlers and remove duplicated semantic name scans.

## Tasks

- [ ] P5.1 Add MutationTargetIndexMetadata with entity, stage, action, policy, and relationship indexes.
- [ ] P5.2 Add shared resolution helpers and diagnostics mapping.
- [ ] P5.3 Refactor DomainMutationContext and selected DomainChange handlers to use unified resolution.
- [ ] P5.4 Require AnalysisResult for mutation-target semantic resolution.

## Primary Files

- Poly/DomainModeling/Evolution/DomainMutationContext.cs
- Poly/DomainModeling/Evolution/DomainChange.cs
- Poly/DomainModeling/Analysis/* for metadata production

## Acceptance Criteria

- [ ] Duplicated target-resolution scans are removed from migrated handlers.
- [ ] Ambiguous or missing targets fail closed with explicit diagnostics.
- [ ] Nullable analysis signatures are removed for migrated semantic handlers.

## Verification

- [ ] Build green.
- [ ] Evolution tests green.
- [ ] New tests for ambiguity and missing-target fail-closed behavior.
