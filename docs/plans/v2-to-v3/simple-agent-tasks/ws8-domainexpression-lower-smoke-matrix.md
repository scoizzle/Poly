# Micro-Task: DomainExpression lowering smoke matrix

**Parent Workstream**: WS8  
**Difficulty**: Small Model Friendly  
**Estimated Tokens**: ~6k  
**Status**: [ ] Not Started

## Objective

Add or extend tests that assert **every** `DomainExpression` factory/kind lowers to a non-null Syntax `Node` without throwing (smoke matrix), catching regressions when WS8 continues.

## Context You Need

- `Poly/DomainModeling/DomainExpression.cs` (and nested expression types)
- `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`
- Existing tests: search `DomainExpressionLowering` under `Poly.Tests`

## Exact Steps

1. List all concrete `DomainExpression` subtypes / factory methods.
2. For each, build a minimal expression (literals + one property access as needed).
3. Run the lowering pass; assert result is non-null `Node` (and optionally a loose type check).
4. Prefer one parameterized / theory-style test or a clear matrix of named tests (`Method_Condition_ExpectedResult` style).
5. Do **not** require VM execution in this task (that is `ws8-e2e-policy-vm-eval.md`).

## Verification

- [ ] Build green
- [ ] New/updated tests pass
- [ ] No production code changes unless a clear lowering bug blocks the matrix (fix the bug if trivial)

## Output

- Tests under `Poly.Tests/DomainModeling/Lowering/` (or adjacent)
- Agent summary noting any kinds that still fail / are skipped with reason

## Out of Scope

- Contract interfaces
- Evolution layer
- Perf
