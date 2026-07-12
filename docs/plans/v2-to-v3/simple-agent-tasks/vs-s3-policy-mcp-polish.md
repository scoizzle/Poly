# Micro-Task: Policy MCP polish (affordances + README)

**Suite:** [`vs-README.md`](vs-README.md) **#3.5**  
**Depends on:** #3.4  
**Parent:** Slice 3  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~3k tokens  
**Status:** [ ] Not Started  

## Objective

Agent UX polish for policy tools: affordances chain, README documents the loop, no dishonest claims.

## Required Reading

- `Poly.Mcp/README.md`
- `V3DomainTools.cs` affordances on create/add/get_policy/evaluate

## Exact Steps

1. Ensure success affordances: after structure → `add_policy`; after add_policy → `get_policy_expression`, `evaluate_policy`.
2. README: short “Policy loop” section with required args and subject key rules.
3. Grep tool Descriptions for overclaims; fix.
4. No new tools.

## Verification

- [ ] README accurate
- [ ] Affordances sensible
- [ ] Existing smokes still green

## Output

- README + small tool affordance tweaks
- Summary: “Slice 3 complete — ready for M2 checkpoint”

## Out of Scope

- Slice 4 effects
- New expression shapes

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
