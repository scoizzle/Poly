# Micro-Task: Domain-attached policy e2e (canonical entity)

**Suite:** [`vs-README.md`](vs-README.md) **#2.5**  
**Depends on:** #1.2 (canonical entity), #2.3  
**Parent:** Slice 2  
**Difficulty:** Small–Medium  
**Estimated Context:** ~6k tokens  
**Status:** [x] Done — DomainAttached_CanonicalPerson_EvaluatesTrueAndFalse  

## Objective

**Definition of done for Slice 2:** build a small `Domain` for the **canonical entity**, attach a `Policy` via evolution/direct API, evaluate true/false via `PolicyEvaluator` — no MCP.

## Required Reading

- Canonical entity from #1.2 / `vs-README.md`
- `DomainEvolution` / `AddPolicyToEntity` fluent API
- `PolicyEvaluator`
- Existing domain-attached tests if any (`PolicyVmEvaluationTests`)

## Exact Steps

1. Create domain: factory + entity + property (e.g. Age/Number) + policy expression.
2. `Apply` succeeds (analysis clean).
3. Evaluate policy from the domain’s policy object true/false on two subjects.
4. Single test method or focused test class named for the canonical entity.
5. Mark Slice 2 complete in summary; do not open MCP work in this task.

## Verification

- [ ] Domain graph + policy + VM eval in one flow
- [ ] True and false
- [ ] Build green

## Output

- Tests (+ tiny API glue only if missing `AddPolicy` already exists — it should)
- Summary: “Slice 2 exit criteria met”

## Out of Scope

- MCP add_policy / evaluate_policy
- Effects
- Relationships

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
