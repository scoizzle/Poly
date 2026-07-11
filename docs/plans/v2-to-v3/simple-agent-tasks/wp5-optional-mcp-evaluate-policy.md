# Micro-Task: Optional MCP EvaluatePolicy tool

**Parent**: WP5  
**Difficulty**: Medium  
**Estimated Tokens**: ~8k  
**Status**: [ ] Not Started  
**Depends on**: `ws8-e2e-policy-vm-eval.md` green

## Objective

Expose a thin MCP tool that evaluates a named policy on an entity using sample JSON/record args — **only if** dogfood needs it. Otherwise skip.

## Exact Steps

1. Confirm core API path from e2e policy tests.
2. Add `evaluate_policy` (or similar) to `V3DomainTools` / query tools:
   - sessionId, entityName, policyName, args (flat or simple JSON object)
3. Call DomainModeling + PolicyEvaluator VM path; return structured true/false + diagnostics.
4. Affordances on failure; no V2.
5. Smoke test in `V3McpSmokeTests`.

## Verification

- [ ] Tool registered
- [ ] Test green
- [ ] Stay under ~25 tools total

## Out of Scope

- Full instance simulation / Dictionary entity model
