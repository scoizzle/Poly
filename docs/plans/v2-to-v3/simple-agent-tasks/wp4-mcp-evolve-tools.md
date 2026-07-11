# Micro-Task: MCP evolve tools (curated atomic set)

**Parent**: WP4  
**Difficulty**: Medium  
**Estimated Tokens**: ~12k  
**Status**: [x] **Done** — apply_evolution removed (polymorphic bag anti-pattern); atomic tools only; diagnostics + affordances on rollback; multi-tool smoke test passes

## Objective

Curated mutate tools: AddEntity, AddProperty, AddStage, AddAction (+ minimal removes if needed) via V3 `Evolve`/`Apply`.

## Exact Steps (original — largely done)

1. Each tool: resolve session → call EvolutionBuilder method(s) → analysis gate → update session on success only.
2. On rollback: return diagnostics + affordances; do not bump domain incorrectly.
3. Flat args; natural descriptions; revision in response.
4. Prefer composition: one tool one intent; optional second task for Scaffold if needed.
5. Stay within overall ~25 tool budget for M2.

## Code-review follow-ups (do these before marking Done)

1. **Remove or redesign `apply_evolution`** — accepting `IReadOnlyList<DomainChange>` is not usable over MCP JSON for agents (polymorphic bag / free-form intent anti-pattern). Prefer atomic tools only for M2, or a **flat typed** intent DTO list with explicit discriminators.
2. **Success message honesty** — if mutation no-ops (missing entity), do not report generic success; fail with recoverable message (may require DomainChange/analyzer fix first).
3. **Diagnostics + affordances** on rollback (same envelope as session/overview tools).
4. **Smoke test** multi-tool path: create → add_entity → add_property → add_stage → add_action → get_entity_detail.
5. Descriptions: add when-not-to-use / type-name examples where thin.

## Verification

- [x] Atomic tools wired through DomainEvolution (as of review)
- [ ] `apply_evolution` removed or replaced with agent-safe design
- [ ] Happy-path multi-tool smoke test
- [ ] No V2 mutators on product path

## Out of Scope

- Actor tools; full effect authoring tools; V2 parity tool count
