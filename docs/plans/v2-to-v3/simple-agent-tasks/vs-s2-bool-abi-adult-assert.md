# Micro-Task: Bool ABI — adult policy asserts real bool

**Suite:** [`vs-README.md`](vs-README.md) **#2.2**  
**Parent:** Slice 2  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~4k tokens  
**Status:** [ ] Not Started  

## Objective

Tests for adult/boolean policies must assert **`bool true`/`false`**, not only `1L`/`0L`, so VM ABI regressions for bool are caught.

## Required Reading

- Policy VM tests that check adult / Age ≥ 18
- `PolicyEvaluator` result extraction (`GetValue<bool>` / `ExecutionResult`)

## Exact Steps

1. Find tests that only compare long `1`/`0` for bool policies.
2. Assert `bool` (or dual-oracle bool) on the product evaluate path.
3. Fix production only if a real bool ABI bug appears (prefer smallest fix in Interpreter result path or emitter — do not redesign).

## Verification

- [ ] At least one adult/bool policy test uses `bool`
- [ ] True and false cases covered if easy
- [ ] Build green

## Output

- Tests (+ minimal prod fix if required)
- Summary

## Out of Scope

- MCP
- Full numeric promotion matrix

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
