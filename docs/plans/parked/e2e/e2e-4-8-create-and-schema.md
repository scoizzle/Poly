# e2e-4-8 — Creatable POST + EnsureCreated

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** 4-7  

## Objective

- POST create when `Entity.Create` is generable. `BadRequest`-only POST for a creatable root is a bug.  
- Non-roots only created via `create in Rel` stay without standalone POST **if** parent create-in exists — document in one guide sentence.  
- Every shipped DBMS pack that seeds also `EnsureCreated` (or startup fails loud). Not SQLite-only.

## Exact steps

1. Tests for creatable-root POST; for create-in-only child (no extra POST); SqlServer/Generic host emits schema create.
2. Smallest Program.cs emit change.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `MinimalApiGenerator.cs` (create routes + Program seed/setup) | `DbContextGenerator` relationship mapping |
| guide one sentence | |

## Status

**Status:** Not Started  
**Claimed by:**  
