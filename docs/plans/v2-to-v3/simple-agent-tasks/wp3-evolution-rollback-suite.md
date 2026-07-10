# Micro-Task: Evolution rollback suite

**Parent**: WP3  
**Difficulty**: Small  
**Estimated Tokens**: ~5k  
**Status**: [ ] Not Started

## Objective

Prove analysis gate + immutability rollback semantics for agents.

## Exact Steps

1. Extend or add tests on `DomainEvolution`:
   - Successful apply returns new domain; caller can keep previous root reference unchanged in content if they retained it
   - Failed apply: `WasRolledBack`, `Domain` equals pre-apply root (same structural content), diagnostics include errors
   - Trace / EVOLUTION_STEP infos present when designed to be
2. Use existing `EvolutionResult` API; do not invent new types.

## Verification

- [ ] Tests green
- [ ] No V2

## Out of Scope

- Trace UX docs (WS4); MCP
