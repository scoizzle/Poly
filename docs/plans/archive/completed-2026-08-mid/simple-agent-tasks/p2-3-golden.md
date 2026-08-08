# P2-3 — Store-linked multi-hop golden

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** P2-2  

## Objective

End-to-end: two relationships, three instances linked, policy `loan book Title is "X"` (or guide-legal spelling) evaluates true/false correctly via evaluate_policy or DomainEntityInstance.

## Exact steps

1. Build domain + store graph.  
2. Assert true when path matches; false when Title wrong or unlinked.  
3. Assert many-middle bare path analysis or runtime fail-closed.

## Verification

- [ ] Golden green  

## File ownership

- Tests primarily; production only if bug found  

## Status

**Status:** Not Started  
