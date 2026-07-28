# Downstream Analysis Consumption Remediation Queue (dacr-*)

Parent: ../downstream-analysis-consumption-remediation.md
Core rule: ../../CORE.md
Gate: ../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md

## Objective

Execute the remediation plan as small slices while enforcing one contract:
semantic downstream paths require AnalysisResult and fail closed when analysis or required metadata is missing.

## Pick Order

1. First unchecked task in Phase 0.
2. Then progress phase-by-phase in order.
3. Run Dacr Gate after each completed phase.

## Phase Status

| Phase | Task File | Status |
|---|---|---|
| Phase 0 | dacr-p0-guardrails.md | [ ] |
| Phase 1 | dacr-p1-lowering-required-analysis.md | [ ] |
| Phase 2 | dacr-p2-mcp-semantic-lookups.md | [ ] |
| Phase 3 | dacr-p3-dslcompiler-semantic-lookups.md | [ ] |
| Phase 4 | dacr-p4-runtime-static-dynamic.md | [ ] |
| Phase 5 | dacr-p5-evolution-target-index.md | [ ] |
| Phase 6 | dacr-p6-contract-enforcement.md | [ ] |
| Gate | dacr-gate.md | [ ] |

## Hard Rules

1. No new semantic path may accept missing AnalysisResult.
2. No semantic fallback scans after migration in a touched area.
3. Structural traversal is allowed only for projection and rendering.
4. Runtime dynamic state stays runtime-owned, but static contracts come from analysis metadata.

## Done Definition (Suite)

1. All phase files are marked complete with notes.
2. Gate file checks are all complete.
3. Build and tests are green.
4. No nullable AnalysisResult signatures remain in targeted downstream semantic APIs.
