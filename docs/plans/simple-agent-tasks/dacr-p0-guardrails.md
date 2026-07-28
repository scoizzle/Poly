# Micro-Task: DACR.P0 - Governance and Safety Guardrails

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Small
Status: [ ] Not Started

## Objective

Prevent further spread of optional-analysis semantics while setting up tracking for legacy fallback sites.

## Tasks

- [ ] P0.1 Add short cross-reference note in all related plans to this queue when relevant.
- [ ] P0.2 Add review rule in plan docs: semantic downstream logic must use metadata lookups.
- [ ] P0.3 Tag existing fallback sites in code with a single marker: DM-META-REMOVE-FALLBACK.
- [ ] P0.4 Add boundary guards in touched entry points: missing AnalysisResult fails closed.

## Acceptance Criteria

- [ ] Fallback marker is used consistently for all identified legacy fallback sites.
- [ ] At least one boundary guard is added in each touched module during later phases.
- [ ] No new semantic code path introduced in this phase accepts null analysis.

## Verification

- [ ] Build green.
- [ ] Existing tests green.

## Notes

Keep this phase light: only guardrails and tracking, no major refactors.
