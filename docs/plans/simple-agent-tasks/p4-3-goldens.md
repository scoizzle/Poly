# P4-3 — Runtime goldens (Any/All + Each regression)

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** P4-1  

## Objective

Prove store notify still implements Any/All when subscriptions are authored via DSL (not only IR fixtures). Each regression remains green.

## Required reading

- DomainInstanceStore notify / subscription dispatch  
- Existing Any/All IR tests if present  

## Exact steps

1. Author domain via parse or evolution with `when any` (and `when all` if cheap).  
2. Link peers; transition; assert fire-once / set semantics.  
3. Regression: `when Rel Stage` (Each) still per-element.  
4. Prefer DomainEntityInstance / subscription product tests.

## Verification

- [ ] Any golden green  
- [ ] Each regression green  
- [ ] All golden optional if time — note if deferred  

## File ownership

- **Edit:** tests primarily; store only if bug found (file fix finding if large)  
- **Do not edit:** guide  

## Status

**Status:** Not Started  
