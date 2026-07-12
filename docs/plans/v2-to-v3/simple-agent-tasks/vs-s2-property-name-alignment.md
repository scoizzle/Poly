# Micro-Task: Policy property name alignment

**Suite:** [`vs-README.md`](vs-README.md) **#2.4**  
**Depends on:** #2.1  
**Parent:** Slice 2  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~4k tokens  
**Status:** [ ] Not Started  

## Objective

Prove that domain property name, DomainExpression `PropertyAccess` name, and subject CLR property name must **match** for correct eval — document + one regression test.

## Required Reading

- How `PropertyAccess` lowers (`DomainExpressionLoweringPass`)
- One policy test that uses property names

## Exact Steps

1. Write a test: policy on `"Age"` with subject property `Age` → success path (may reuse #2.3).
2. Write a negative or doc test: mismatch name fails or returns wrong — prefer **clear failure** if easy; else document “names must match” in helper XML doc.
3. One-line note in `PolicySubject` or `PolicyEvaluator` remarks: property names must align with domain/DE.

## Verification

- [ ] Alignment documented
- [ ] At least one test ties names together
- [ ] Build green

## Output

- Test + small doc comment
- Summary

## Out of Scope

- Fuzzy name matching / case-insensitive map
- MCP

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
