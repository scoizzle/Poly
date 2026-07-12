# Micro-Task: Add Explicit Test for NodeId Preservation on Unchanged Subtrees

**Parent Workstream**: WS1 (Consolidated)  
**Difficulty**: Small  
**Estimated Context**: < 6k tokens

## Objective

Add a focused test that proves when you evolve a domain with a change that only touches one part, untouched Entities (and their children) retain their exact original `Node.Id` values in the resulting root.

## Required Reading

- Core principles (build working code before abstractions)
- `Poly/Syntax/Node.cs` (Id property)
- The applicator implementation in `Poly/DomainModeling/Evolution/DomainEvolution.cs`
- Existing applicator tests for pattern

## Exact Steps

1. In the applicator test file (or a new focused one), create a domain with 2+ Entities.
2. Capture the Id of an untouched Entity before evolution.
3. Apply a change that only affects the other entity (e.g. add property to one).
4. In the result root, assert that the untouched Entity has the exact same `.Id` value.
5. Optionally also check a property inside it if easy.

## Verification

- Test passes and demonstrates preservation.
- No new abstractions added just for the test.

## Output

Updated test file with a clear NodeId continuity test exercising the current applicator logic.

## Status

**Claimed by**:  
**Status**: Superseded (2026-07-10) — evolution foundation delivered; see master-roadmap.md
