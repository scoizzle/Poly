# Micro-Task: Wire PolicySubject into product PolicyEvaluator

**Suite:** [`vs-README.md`](vs-README.md) **#0.3**  
**Parent:** Slice 0  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~4k tokens  
**Status:** [ ] Not Started  

## Objective

`PolicyEvaluator.Evaluate` / `CompileVMPredicate` must call subject validation so Dict/Expando (and other forbidden subjects) fail on the **product** path, not only in isolated tests.

## Required Reading

- `Poly/DomainModeling/Lowering/PolicyEvaluator.cs`
- `Poly/DomainModeling/Lowering/PolicySubject.cs` (or TestHelpers if helper lives only in tests — prefer product code)
- `Poly.Tests/DomainModeling/Lowering/PolicySubjectInvariantTests.cs` if present

## Exact Steps

1. Find `PolicySubject.Validate` (or equivalent). If only test-only, move or re-export a product API under `DomainModeling/Lowering/`.
2. Call validation at the start of `CompileVMPredicate` and/or `Evaluate` (and dual-oracle if it takes a subject).
3. Add a product-path test: `Evaluate` with `Dictionary<string, object>` **throws** or returns clear failure (match existing helper style).
4. Ensure legitimate CLR records still evaluate.

## Verification

- [ ] Product path rejects Dict/Expando
- [ ] Existing policy VM tests still green
- [ ] Build green

## Output

- `PolicyEvaluator.cs` (+ `PolicySubject.cs` if needed)
- Tests
- Summary

## Out of Scope

- MCP evaluate_policy tool (Slice 3)
- Reflection.Emit redesign

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
