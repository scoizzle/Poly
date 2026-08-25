# e2e-x-1 — Reserved generated names

**Difficulty:** M  
**Status:** `[ ]`  
**Fleet:** P3-10 · decide 07-F9 (`Create` action)

## Objective

C# keywords, `CurrentStage`, `DomainResult`, `namespace`/`event` as entity/prop names, and (if locked) action `Create` fail at **analysis**. No raw emission.

## Exact steps

1. Write the reserved set in the test (one test per name class).  
2. Decide 07-F9: reserve `Create` as an action name (recommended — fail-closed) or document overload luck. Put the decision in the test comment.  
3. Structural analysis reject. Do not sanitize by renaming (silent divergence).

## File ownership

| Edit | Do not edit |
|------|-------------|
| analysis name pass (existing structure/name analyzer) | `MinimalApiGenerator` |
| tests | Q3′ |

## Status

**Status:** Not Started  
**Claimed by:**  
