# Micro-Task: MCP `add_policy` tool

**Suite:** [`vs-README.md`](vs-README.md) **#3.2**  
**Depends on:** #3.1  
**Parent:** Slice 3  
**Difficulty:** Small–Medium  
**Estimated Context:** ~5k tokens  
**Status:** [ ] Not Started  

## Objective

Add MCP tool `add_policy` that attaches a policy to an entity via **DomainEvolution only** (thin adapter).

## Required Reading

- `Poly.Mcp/Tools/V3DomainTools.cs` — existing evolve pattern (`Evolve` helper)
- `Poly.Mcp/Sessions/McpSessionStore.cs`
- Contract from #3.1
- `EvolutionBuilder.AddPolicyToEntity`

## Exact Steps

1. Add `[McpServerTool(Name = "add_policy")]` with sessionId, entityName, policyName, constrained expression args.
2. Call session evolve → `AddPolicyToEntity` → analysis gate; return diagnostics on failure.
3. Description honest: what expression shapes are allowed.
4. Affordances on success include `get_policy_expression` / future evaluate.
5. Unit/smoke: add policy then `get_entity_detail` or `get_policy_expression` shows it.

## Verification

- [ ] No domain logic in tool beyond mapping args
- [ ] Failure is recoverable (diagnostics)
- [ ] Tests green

## Output

- `V3DomainTools.cs` + tests
- Summary

## Out of Scope

- evaluate_policy (#3.3)
- Free-form AST JSON

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
