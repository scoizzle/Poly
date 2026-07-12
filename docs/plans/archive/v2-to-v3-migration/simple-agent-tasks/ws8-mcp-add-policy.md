# Micro-Task: MCP `add_policy` tool

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#7**  
**Difficulty**: Medium  
**Estimated Tokens**: ~8k  
**Status**: [ ] Not Started  
**Depends on**: Prefer `ws8-mcp-add-policy-expression-contract.md` first (or write contract in the same PR)

## Objective

Agents can **attach a policy** to an entity via MCP without calling `DomainEvolution` from tests.

## Exact Steps

1. Follow expression contract in `spikes/mcp-add-policy-expression-contract.md` (create via task #7a if missing).
2. Add tool e.g. `add_policy` on `V3EvolveTool` (or policy-focused type):
   - `sessionId`, `entityName`, `policyName`
   - Expression: **only** shapes in the contract — **not** free-form DomainExpression AST JSON
   - Map to `DomainExpression` + `DomainEvolution.Evolve().AddPolicyToEntity(...).Apply()`
2. On success: update session, return revision + affordances (`get_policy_expression`, later `evaluate_policy`).
3. On failure: no revision bump; diagnostics + affordances (entity missing, analysis reject).
4. Descriptions honest — what expression shapes are supported.
5. Smoke tests in `V3McpSmokeTests`:
   - create session → add entity → add property → **add_policy** → `get_policy_expression` finds it
   - missing entity → fail
6. Stay under ~25 tools; no V2.

## Verification

- [ ] Tool registered
- [ ] Smoke tests green
- [ ] Domain graph has policy after success
- [ ] No direct `DomainEvolution` required in the happy-path smoke (only tool APIs)

## Out of Scope

- Full arbitrary DomainExpression JSON AST
- Stage/property-level policies (entity-level is enough for A+)
- VM evaluation (task #8)
