# e2e-5-3 — Analyze, store, export the field

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** 5-2  

## Objective

Type ref resolves. Constraints on value-type fields fail closed. Instance assign/read works. Exported C# compiles. Pick flattened properties **or** a nested record from the first working path — do not extract a framework.

## Exact steps

1. Tests: analysis resolve; assign/read; export compile.  
2. Smallest lowering/export that matches the runtime shape.

## File ownership

| Edit | Do not edit |
|------|-------------|
| analysis type-ref, instance bag, exporter property emit | OwnedAccess product syntax |
| tests | slice 6 contracts |

## Status

**Status:** Not Started  
**Claimed by:**  
