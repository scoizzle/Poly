# Micro-Task: Sever PolicyEvaluator from V2

**Parent**: WP1  
**Difficulty**: Small  
**Estimated Tokens**: ~4k  
**Status**: [ ] Not Started

## Objective

`Poly/DomainModeling/Lowering/PolicyEvaluator.cs` must not depend on `Poly.Data.Modeling`.

## Exact Steps

1. Open `PolicyEvaluator.cs`; remove `using Poly.Data.Modeling`.
2. Ensure `Policy` / `DomainExpression` resolve to V3 types only.
3. Fix any compile breaks (ambiguous Policy, etc.).
4. Run existing DomainModeling lowering/VM tests; fix if needed.
5. Grep `Poly/DomainModeling` for `Poly.Data.Modeling` — zero hits.

## Verification

- [ ] Build green
- [ ] Lowering tests pass
- [ ] No V2 usings under DomainModeling

## Out of Scope

- New policy features; MCP
