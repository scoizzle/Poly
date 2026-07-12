# Micro-Task: Policy subject helper + reject Dict/Expando

**Suite:** [`vs-README.md`](vs-README.md) **#2.1**  
**Depends on:** **#0.3** Done  
**Parent:** Slice 2  
**Difficulty:** Small–Medium  
**Estimated Context:** ~5k tokens  
**Status:** [ ] Not Started  

## Objective

Product-facing way to build a policy subject with **real CLR properties** and non-null defaults; Dict/Expando rejected at evaluate boundary.

## Required Reading

- `Poly/DomainModeling/Lowering/PolicySubject.cs`
- `Poly/DomainModeling/Lowering/PolicyEvaluator.cs`
- `Poly.Tests/TestHelpers/PolicyTestSubjects.cs` if present
- Existing invariant/spike tests under `Poly.Tests/DomainModeling/Lowering/`

## Exact Steps

1. Ensure a helper exists for tests/MCP later: name→value bag → subject with real properties (record / StrictBag / proven Emit). **Not** Dictionary as the evaluate target.
2. Missing keys → non-null defaults (`0`, `""`, `false`) for primitives used in policies.
3. Confirm product `Evaluate` rejects Dict/Expando (from #0.3); add test if missing.
4. One happy-path test: Age-style policy true/false via helper (can be minimal; full e2e is #2.3/#2.5).

## Verification

- [ ] Helper used by ≥1 test
- [ ] Dict/Expando fail on product evaluate
- [ ] Build + policy tests green

## Output

- DomainModeling lowering helpers + tests
- Summary

## Out of Scope

- MCP tools
- Full Emit generator redesign unless already there and broken

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
