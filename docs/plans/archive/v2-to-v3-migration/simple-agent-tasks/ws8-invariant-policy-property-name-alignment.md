# Micro-Task: Invariant — policy property names align with subject

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#6g**  
**Difficulty**: Small  
**Estimated Tokens**: ~4k  
**Status**: [ ] Not Started  
**Depends on**: Prefer with or after `ws8-invariant-policy-subject-types.md` (#6d)

## Objective

Document and lightly enforce: **DomainExpression property names** used in policies must match **CLR subject property names** (and domain entity property names when policy is domain-attached). Silent wrong Members are a known failure mode (Dict/Expando); mis-typed record field names are the same class of bug.

## Invariant

When evaluating a policy:

1. Every `PropertyAccess` name in the policy expression should exist on the subject type (or bag builder slots).
2. For domain-attached policies, expression property names should exist on the entity (or be explicitly allowed parameters).
3. Missing/mismatched names must fail **loudly** at subject-build or eval time — not return a wrong bool.

## Exact Steps

1. Add short invariant section to `spikes/policy-sample-subject.md` (or DomainModeling README lowering section).
2. In subject helper (#6d) or evaluate_policy (#8):
   - When building from a bag + entity, only set known entity property names; unknown agent keys → clear error or ignore with diagnostic.
   - Optional: walk policy expression for PropertyAccess names and require bag contains them (or defaults applied).
3. Test: evaluate with bag missing a required property name used in expression — either default documented behavior or explicit failure message (pick one and test it).
4. Test: wrong property name in bag (e.g. `age` vs `Age`) does not silently pass adult guard when Age missing (defaults to 0 → false is OK if documented).

## Verification

- [ ] Invariant written down
- [ ] At least one test locks missing/wrong name behavior
- [ ] No silent “true” from unresolved Members if avoidable

## Out of Scope

- Full static analysis of all policies in the domain analyzer (unless trivial)
- Fixing Owned/RelationshipNav VM gaps
