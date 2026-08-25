# Micro-Task: add_policy success affordances include evaluate_policy

**Suite:** [`vs-README.md`](vs-README.md) **#pm2-2**  
**Parent:** Post-M2  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~2k tokens  
**Status:** [ ] Not Started  

## Objective

After successful `add_policy`, agent affordances should include `evaluate_policy` (and keep `get_policy_expression` / `get_entity_detail`) so the M2 agent loop is discoverable without reading docs.

## Required Reading

- `Poly.Mcp/Tools/V3DomainTools.cs` — `AddPolicy` success return  
- `Poly.Mcp/README.md` policy section if present  

## Exact Steps

1. On `AddPolicy` success, set Affordances to include `evaluate_policy`, `get_policy_expression`, `get_entity_detail` (order free).  
2. Optionally on `get_policy_expression` success, include `evaluate_policy`.  
3. Smoke or unit assert affordance list contains `evaluate_policy`.  

## Verification

- [ ] Affordance present after add_policy success  
- [ ] Existing smokes green  

## Output

- Small tool + test change  
- Summary  

## Out of Scope

- Multi-property evaluate (pm2-1)  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes:**
