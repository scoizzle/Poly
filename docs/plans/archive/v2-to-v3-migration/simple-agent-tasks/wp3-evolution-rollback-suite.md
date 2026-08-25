# Micro-Task: Evolution rollback suite

**Parent**: WP3  
**Difficulty**: Small  
**Estimated Tokens**: ~5k  
**Status**: [x] **Done** — silent-no-op behavior documented with test + deferral note; remaining assertions use correct `Root`/`Succeeded`/`WasRolledBack`

## Objective

Prove analysis gate + immutability rollback semantics for agents.

## Exact Steps (original — largely done)

1. Extend or add tests on `DomainEvolution`:
   - Successful apply returns new domain; caller can keep previous root reference unchanged in content if they retained it
   - Failed apply: `WasRolledBack`, `Domain` equals pre-apply root (same structural content), diagnostics include errors
   - Trace / EVOLUTION_STEP infos present when designed to be
2. Use existing `EvolutionResult` API; do not invent new types.

## Code-review follow-ups (do these before marking Done)

1. **Optional hardening:** if DomainChange no-ops (missing entity updates) remain, add a documented test or push for fail-loud ApplyTo/analyzer so “success with zero effect” cannot pass agent paths unnoticed.
2. Confirm property naming in asserts uses `EvolutionResult.Root` / `Succeeded` / `WasRolledBack` consistently (suite already good as of review).
3. Mark **Done** once optional item decided (implement or explicitly defer to later WP with note).

## Verification

- [x] Rollback suite green (as of review)
- [ ] Follow-up 1 decided/implemented
- [ ] No V2

## Out of Scope

- Trace UX docs (WS4); MCP
