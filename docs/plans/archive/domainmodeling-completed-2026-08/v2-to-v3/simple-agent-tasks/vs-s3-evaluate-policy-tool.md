# Micro-Task: MCP `evaluate_policy` tool (VM bool)

**Suite:** [`vs-README.md`](vs-README.md) **#3.3**  
**Depends on:** #3.2, Slice 2  
**Parent:** Slice 3  
**Difficulty:** Small–Medium  
**Estimated Context:** ~6k tokens  
**Status:** [x] Done — EvaluatePolicy MCP tool (Age subject)  

## Objective

Add MCP tool that evaluates a named entity policy on sample property values and returns a **VM bool**. Never claim success without a real result. Never use Dict/Expando as the VM subject.

## Required Reading

- `PolicyEvaluator` + subject helper (Slice 2)
- `V3DomainTools.cs` — `get_policy_expression`
- Session store

## Exact Steps

1. Tool args: sessionId, entityName, policyName, sample values (flat map or constrained fields matching #3.1 property names).
2. Build subject via **product helper** (Slice 2); call `PolicyEvaluator.Evaluate`.
3. Response: `success`, `result: true|false`, message; failures include diagnostics.
4. Description: “Evaluates on VM; sample keys must match property names.”
5. Tests: true case + false case; reject if policy missing.

## Verification

- [ ] Returns actual bool from VM path
- [ ] No Dict/Expando subject
- [ ] Tool honesty (name/description/success match)
- [ ] Tests green

## Output

- MCP tool + tests
- Summary

## Out of Scope

- Caching compiled predicates (perf)
- Dual-oracle inside MCP (core tests already cover)

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
