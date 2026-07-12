# Micro-Task: MCP evolve tools (curated atomic set)

**Parent**: WP4  
**Difficulty**: Medium  
**Estimated Tokens**: ~12k  
**Status**: [x] **Done** — no-op honesty guard + tests (2026-07-10 double-check)  
**Last review**: 2026-07-10 (third pass — residual closed)

## Objective

Curated mutate tools: AddEntity, AddProperty, AddStage, AddAction (+ minimal removes if needed) via V3 `Evolve`/`Apply`.

## Verification (closed)

- [x] Atomic tools via DomainEvolution only
- [x] `apply_evolution` removed
- [x] Happy-path multi-tool smoke (`FullAgentPath_CreateToEntityDetail`)
- [x] No V2 mutators on product path
- [x] Fingerprint guard: missing-target no-op → `Success: false`, revision **not** bumped
- [x] Tests: `AddPropertyToMissingEntity_ReportsFailure_WithoutBumpingRevision`, stage/action variants (12/12 V3McpSmoke pass)

## Residual deferred (not blocking)

- DomainChange fail-loud for missing targets (post-M2 / WP9) — MCP guard is the M2 fix
