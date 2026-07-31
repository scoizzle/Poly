# Downstream Analysis Consumption Remediation Queue (dacr-*)

Parent: ../downstream-analysis-consumption-remediation.md
Core rule: ../../CORE.md
Gate: ../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md

## Objective

Execute the remediation plan as small slices while enforcing one contract:
semantic downstream paths require AnalysisResult and fail closed when analysis or required metadata is missing.

## Pick Order

1. Follow-ups in [`dacr-followups-2026-07-30.md`](./dacr-followups-2026-07-30.md): r1–r5 code closed (r6 re-verify); open **F32–F33** docs honesty only (optional F34–F35).
2. Suite residual (Done Definition item 4): remove remaining `DM-META-REMOVE-FALLBACK` scans when AnalysisResult is universal — not a pick-order blocker for “DACR helpers green.”
3. Run Dacr Gate after each completed phase or follow-up slice.

## Phase Status

| Phase | Task File | Status |
|---|---|---|
| Phase 0 | dacr-p0-guardrails.md | [x] |
| Phase 1 | dacr-p1-lowering-required-analysis.md | [x] |
| Phase 2 | dacr-p2-mcp-semantic-lookups.md | [~] follow-ups closed |
| Phase 3 | dacr-p3-dslcompiler-semantic-lookups.md | [x] |
| Phase 4 | dacr-p4-runtime-static-dynamic.md | [~] follow-ups closed |
| Phase 5 | dacr-p5-evolution-target-index.md | [x] |
| Phase 6 | dacr-p6-contract-enforcement.md | [~] |
| Gate | dacr-gate.md | [~] |
| Follow-ups | dacr-followups-2026-07-30.md | [~] r5 code closed; r6 F32–F33 open (docs) |

## Done Definition (Suite)

1. All phase files are marked complete with notes.
2. Gate file checks are all complete.
3. Build and tests are green.
4. All DM-META-REMOVE-FALLBACK markers resolved (fallback scans removed).

> **Current status (2026-07-30):** Metadata-first helpers and primary paths are in place.
> Fallback scans remain tagged with `DM-META-REMOVE-FALLBACK` (suite Done Definition still
> requires their removal). Active residual work is tracked in
> [`dacr-followups-2026-07-30.md`](./dacr-followups-2026-07-30.md). Historical r1 findings:
> [`dacr-local-review-2026-07-30.md`](./dacr-local-review-2026-07-30.md).
