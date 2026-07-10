# Micro-Task: MCP evolve tools (curated atomic set)

**Parent**: WP4  
**Difficulty**: Medium  
**Estimated Tokens**: ~12k  
**Status**: [ ] Not Started

## Objective

Curated mutate tools: AddEntity, AddProperty, AddStage, AddAction (+ minimal removes if needed) via V3 `Evolve`/`Apply`.

## Exact Steps

1. Each tool: resolve session → call EvolutionBuilder method(s) → analysis gate → update session on success only.
2. On rollback: return diagnostics + affordances; do not bump domain incorrectly.
3. Flat args; natural descriptions; revision in response.
4. Prefer composition: one tool one intent; optional second task for Scaffold if needed.
5. Stay within overall ~25 tool budget for M2.

## Verification

- [ ] Happy path multi-tool sequence works (create → add entity → property → stage → action → overview)
- [ ] Invalid add returns recoverable error
- [ ] No V2 mutators

## Out of Scope

- Actor tools; full effect authoring tools; V2 parity tool count
