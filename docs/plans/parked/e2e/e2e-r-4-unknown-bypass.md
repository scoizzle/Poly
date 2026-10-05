# e2e-r-4 — Close ExpressionTypeAnalyzer Unknown-skip

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** r-1  
**Fleet:** P2-2  

## Objective

Unresolvable identifiers on assign/arg/if/binder paths fail closed instead of `Unknown` skip.

## Exact steps

1. One failing test per form in `probes/fleet-eval` `expr-f1…f9` (or equivalent): invoke-arg caller-prop/param without props; binder-root unknown target props; unresolvable assign RHS.
2. Thread props/parameters into invoke-arg inference. Report unknown identifiers — no `Unknown`-skip on those paths.
3. If-condition type-check can wait for r-5 if you only touch invoke/assign here. Do not skip if the same method already walks if.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs` | parser |
| tests under Analysis | exporter |

## Status

**Status:** Not Started  
**Claimed by:**  
