# e2e-1-1 — Store/instance unique enforce

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** e2e-r gate (EntityInstance free)  

## Objective

Colliding `unique` values fail loud on create/assign when a store is attached. Same posture as `required` / `pattern`.

## Exact steps

1. Tests: `Create_DuplicateUnique_FailsClosed`, `Assign_DuplicateUnique_FailsClosed` (second instance, same unique property).
2. Enforce on `DomainEntityInstance` write path. Query existing instances via **public** store API. If a lookup is missing, add a small query method — do **not** edit `NotifyTransition` or subscription loops.
3. No store → keep current standalone behavior or fail closed if unique cannot be checked; document the choice in the test name. Prefer: unique check only when store-attached; standalone Create without store does not pretend uniqueness.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainEntityInstance.cs` (write / constraint path) | `NotifyTransition` |
| optional small **query** on `DomainInstanceStore` | exporter (task 2) |
| tests | `DbContextGenerator` |

## Status

**Status:** Not Started  
**Claimed by:**  
