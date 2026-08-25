# Micro-Task: Rewrite first V2 demo/benchmark off V2

**Parent**: WP7  
**Difficulty**: Medium  
**Estimated Tokens**: ~10k  
**Status**: **Superseded** — V2 benchmark demos deleted; V3 demos under `Poly/DomainModeling/Examples/Demos/`  
**Depends on**: —

## Objective

Move **one** demo or benchmark entrypoint from V2 to V3 (or delete if obsolete). Prefer the highest-value / most-referenced demo from the inventory.

## Exact Steps

1. Pick the first demo/benchmark from inventory **Port** list under demos (e.g. Library or PersonLifecycle style under `Poly.Benchmarks/DomainModeling/`).
2. Rewrite construction to `DomainFactory` + `DomainEvolution.Evolve()` (or `DomainBuilder` if already V3-compatible).
3. Remove `Poly.Data.Modeling` usings from that path.
4. Ensure the demo still runs (or is clearly skipped with a comment if harness-only).
5. Note in inventory: demo ported.

## Verification

- [ ] Chosen demo compiles without V2
- [ ] Build green
- [ ] Inventory updated

## Out of Scope

- Porting every demo in one task
- Full WP8 delete
