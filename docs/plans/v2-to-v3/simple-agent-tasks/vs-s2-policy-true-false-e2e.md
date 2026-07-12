# Micro-Task: Policy true and false on working subject

**Suite:** [`vs-README.md`](vs-README.md) **#2.3**  
**Depends on:** #2.1 (and #2.2 preferred)  
**Parent:** Slice 2  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~5k tokens  
**Status:** [ ] Not Started  

## Objective

One clear e2e on **direct API**: same policy evaluates **true** for one subject and **false** for another (e.g. Age ≥ 18), VM-primary.

## Required Reading

- `PolicyEvaluator.cs`
- Subject helper from #2.1
- Existing `PolicyVmEvaluationTests.cs`

## Exact Steps

1. Policy: e.g. `Age >= 18` (or existing MatchNumeric pattern that works).
2. Subject A: adult → `Evaluate` true.
3. Subject B: minor → `Evaluate` false.
4. Prefer dual-oracle once if both engines claim support; product assert is VM.
5. Name test `Evaluate_AgePolicy_TrueAndFalse_ExpectedResults` (or similar TUnit style).

## Verification

- [ ] True and false both asserted
- [ ] No Dictionary subject
- [ ] Build green

## Output

- Test file update
- Summary

## Out of Scope

- MCP
- Domain entity graph attachment (that is #2.5)

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
