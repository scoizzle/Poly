# DomainAuthoringContext Removal Queue (dar-*)

Parent: ../domain-authoring-context-removal-plan.md
Core rule: ../../CORE.md
Gate: ../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md

## Objective

Remove `DomainAuthoringContext` as a mutable analyzer/evolution/session control
surface and converge on one analyzer system definition with explicit immutable
inputs.

## Pick Order

1. First unchecked task in Phase A.
2. Progress phase-by-phase through Phase G.
3. Run DAR gate after each phase completion.

## Phase Status

| Phase | Task File | Status |
|---|---|---|
| Phase A | dar-a1-freeze-and-guardrails.md | [x] |
| Phase B | dar-b1-explicit-analysis-inputs.md | [x] |
| Phase C | dar-c1-converge-analyzer-api.md | [x] |
| Phase D | dar-d1-evolution-context-removal.md | [x] |
| Phase E | dar-e1-mcp-session-inputs.md | [x] |
| Phase F | dar-f1-dslcompiler-pack-migration.md | [x] |
| Phase G | dar-g1-final-removal-and-cleanup.md | [x] |
| Gate | dar-gate.md | [x] |

## Hard Rules

1. One analyzer system definition only; no context-based branching.
2. No new mutable singleton/session context for analyzer behavior.
3. Pack variance must flow through explicit immutable inputs/descriptors.
4. Fail closed on invalid extension ordering, missing required metadata, or
   missing inputs in semantic paths.

## Done Definition (Suite)

1. All A1 through G1 tasks are marked complete with notes.
2. Gate checks are all complete.
3. `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` and
   `dotnet run --project Poly.Tests/Poly.Tests.csproj` are green.
4. No remaining production references to `DomainAuthoringContext` outside
   explicitly documented compatibility window artifacts.
