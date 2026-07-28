# Micro-Task: DACR.P6 - Contract Enforcement and Cleanup

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Medium
Status: [ ] Not Started
Prereq: DACR.P1-P5 complete

## Objective

Finalize AnalysisResult-required contracts and remove legacy optional signatures for downstream semantic APIs.

## Tasks

- [ ] P6.1 Remove nullable AnalysisResult parameters in downstream semantic APIs.
- [ ] P6.2 Delete compatibility shims that permit semantic execution without analysis.
- [ ] P6.3 Update tests to assert boundary fail-closed behavior for missing analysis.

## Acceptance Criteria

- [ ] No semantic downstream route in scope accepts missing AnalysisResult.
- [ ] No fallback scan remains in scope-marked semantic APIs.
- [ ] All boundary checks are explicit and tested.

## Verification

- [ ] Build green.
- [ ] Full tests green.
- [ ] Search for nullable analysis in semantic API signatures in scoped files returns none.
