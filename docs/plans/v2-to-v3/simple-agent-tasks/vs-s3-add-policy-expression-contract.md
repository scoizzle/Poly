# Micro-Task: Constrained expression contract for add_policy

**Suite:** [`vs-README.md`](vs-README.md) **#3.1**  
**Depends on:** Slice 2 Done  
**Parent:** Slice 3  
**Difficulty:** Small–Medium  
**Estimated Context:** ~5k tokens  
**Status:** [x] Done — PolicyExpressionContract + Parser + 14 tests  

## Objective

Define a **small, agent-safe** JSON (or flat args) contract for policy expressions — **no** free-form AST bags agents invent.

## Required Reading

- `spikes/mcp-guiding-principles.md` — constrained inputs (skim)
- `DomainExpression` factories in `Poly/DomainModeling/DomainExpression.cs`
- Slice 2 policy shapes (Age ≥ N comparison)

## Exact Steps

1. Propose minimal contract covering Slice 2 needs only, e.g.:
   - property name + op (`>=`, `==`, …) + literal value  
   - or named templates: `AgeAtLeast` with `value`
2. Document in `Poly.Mcp/README.md` or a short comment on the forthcoming tool.
3. Implement a pure function: contract → `DomainExpression` (in DomainModeling or MCP-local helper that only builds DE).
4. Unit tests: valid payload → DE; invalid payload → clear error.
5. Do **not** implement the MCP tool yet if easier to stop here — tool is #3.2. Prefer shared parser used by #3.2.

## Verification

- [ ] Only constrained shapes accepted
- [ ] Maps to DomainExpression used by Slice 2
- [ ] Tests green

## Output

- Parser/helper + tests + short docs
- Summary

## Out of Scope

- Full DE grammar
- evaluate_policy tool

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
