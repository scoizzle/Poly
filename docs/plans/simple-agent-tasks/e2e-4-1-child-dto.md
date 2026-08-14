# e2e-4-1 — Child action `dto` parameter

**Difficulty:** S  
**Status:** `[x]` 2026-08-13 — parent-ctx action lambdas declare `{Action}Dto`  
**Fleet:** P3-1 · **Needs:** e2e-g0  

## Objective

Parent-ctx branch in `AppendActionEndpointStatements` declares `{Action}Dto`. CS0103 gone.

## Exact steps

1. Full-solution compile of `probes/fleet-eval/09-transport/warehouse.poly` is the acceptance (will still fail later tasks — this task kills CS0103 `dto` only if you can isolate; otherwise add a focused generator test that the child action lambda has a `dto` param).
2. Shared body and handler signature stay in lockstep.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `src/Poly.DslCompiler/MinimalApiGenerator.cs` (`AppendActionEndpointStatements`) | `DbContextGenerator` |
| tests | `DomainToCSharpExporter` |

## Status

**Status:** Not Started  
**Claimed by:**  
