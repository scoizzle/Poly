# DAS W4.1 — Runtime: no semantic fallback scans when Domain bound

**Wave:** W4 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) P8, §5.1, W4  
**Difficulty:** Medium  
**Status:** `[ ]`  
**Prereq:** W1 gate, W2 gate  

## Objective

When `Domain` is non-null, runtime uses catalog/helpers only. Remove `DM-META-REMOVE-FALLBACK` scans in `DomainEntityInstance` / `DomainInstanceStore` (and related) for action/stage/relationship semantic resolution.

## Tasks

- [ ] W4.1.1 Grep markers under DomainEntityInstance / DomainInstanceStore; delete scan branches when analysis present/domain bound.
- [ ] W4.1.2 Define standalone (`Domain == null`) contract: unsupported for semantic dispatch **or** reduced documented surface—no silent SA dual implementation.
- [ ] W4.1.3 Fail closed if catalog/required bags missing.
- [ ] W4.1.4 Tests: domain-bound paths; standalone behavior explicit.

## Acceptance criteria

- [ ] Zero fallback markers in those runtime files (or ADR exception).
- [ ] Build + tests green; sibling-path N/A (single path).

## Progress notes

(empty)
