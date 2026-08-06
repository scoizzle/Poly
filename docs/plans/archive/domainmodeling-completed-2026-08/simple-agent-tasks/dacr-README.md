# Downstream Analysis Consumption Remediation Queue (dacr-*)

Parent: ../downstream-analysis-consumption-remediation.md
Core rule: ../../CORE.md
Gate: ../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md

## Objective

Execute the remediation plan as small slices while enforcing one contract:
semantic downstream paths require AnalysisResult and fail closed when analysis or required metadata is missing.

## Pick Order

1. Follow-ups in [`dacr-followups-2026-07-30.md`](./dacr-followups-2026-07-30.md): r1–r5 code closed; r6 docs — F33 closed via DAS W4.3 EffectLowering fail-closed.
2. Suite Done Definition item 4 **closed** via DAS W4 (markers 0 + analysis-present soft dual paths removed on scoped semantic routes).
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
| Phase 6 | dacr-p6-contract-enforcement.md | [~] fallback AC via DAS W4; nullable-API cleanup residual |
| Gate | dacr-gate.md | [x] G2 + item 4 closed via DAS W4.3 |
| Follow-ups | dacr-followups-2026-07-30.md | [~] r5 code closed; F33 closed; F34 optional hygiene open |

## Done Definition (Suite)

1. All phase files are marked complete with notes.
2. Gate file checks are all complete.
3. Build and tests are green.
4. [x] All `DM-META-REMOVE-FALLBACK` markers resolved **and** fallback scans removed — **via DAS W4**: markers `rg **/*.cs` = 0; runtime/MCP/export/evolution/MinimalApi monopaths; `EffectLoweringPass.GetConstructorParameterOrder` fail-closed under analysis (ESM required). Evidence: [`das-w4-3-marker-zero-and-dacr-close.md`](./das-w4-3-marker-zero-and-dacr-close.md), [`das-gate.md`](./das-gate.md) G4.1–G4.5, [`dacr-gate.md`](./dacr-gate.md) G2.

> **Current status (2026-07-31, post W4.3 re-open fix):** Markers 0; analysis-present soft ctor-order dual path deleted; item 4 + G2 closed.
> Standalone (`Domain == null` / analysis-null) reduced contracts remain DAS non-goals.
> Historical r1 findings: [`dacr-local-review-2026-07-30.md`](./dacr-local-review-2026-07-30.md).
