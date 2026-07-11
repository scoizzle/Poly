# Micro-Task: DomainExpression lower smoke matrix

**Parent**: WP5 / WS8  
**Difficulty**: Small–Medium  
**Estimated Tokens**: ~6k  
**Status**: [ ] Not Started  
**Depends on**: Prefer after or with `ws8-e2e-policy-vm-eval` if overlapping

## Objective

Regression table: each **M2-relevant** `DomainExpression` node kind lowers and (where feasible) executes on VM without throwing.

## Exact Steps

1. Inventory node kinds in `DomainExpression.cs` (Property, Parameter, Literal, arithmetic, comparisons, And/Or/Not, Exists, DateOp, RelationshipNav, Owned).
2. For each kind used by policies/authoring smoke: one lower smoke and one VM execute where already supported.
3. Skip or mark `[Ignore]` / documented gap only if lowering throws by design — list gaps in agent-summary.
4. Prefer extending `DomainExpressionVmExecutionTests` / lowering tests rather than a new framework.

## Verification

- [ ] Matrix covered or gaps listed
- [ ] Tests green
- [ ] No new DE node kinds without a consumer

## Out of Scope

- Full action/effect program lowering
