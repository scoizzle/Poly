# Micro-Task: MCP-only policy e2e smoke (A+ gate)

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#9**  
**Difficulty**: Small  
**Estimated Tokens**: ~4k  
**Status**: [ ] Not Started  
**Depends on**: `ws8-mcp-add-policy.md` + `ws8-mcp-evaluate-policy-vm.md`

## Objective

One smoke test that proves the **agent loop** without touching `DomainEvolution` in the test body:

```
create_domain_session
→ add_entity / add_property
→ add_policy
→ get_policy_expression   (optional assert)
→ evaluate_policy { … } → true
→ evaluate_policy { … } → false
```

## Exact Steps

1. Add `V3McpSmokeTests` method e.g. `AgentPolicyLoop_AddAndEvaluate_OnVm`.
2. Use **only** MCP tool static methods (`V3SessionTool`, `V3EvolveTool`, `V3EvalTool` / policy tools).
3. Assert revisions/success flags and `data.result` bools.
4. No `new DomainEvolution` / `DomainFactory` in this test.

## Verification

- [ ] Test green
- [ ] No core evolve APIs in test body
- [ ] Documents the A+ agent path for future agents

## Out of Scope

- New features beyond wiring already landed in #7–#8
