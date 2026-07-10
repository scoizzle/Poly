# Micro-Task: Retire V2 DomainTools product path

**Parent**: WP4  
**Difficulty**: Medium  
**Estimated Tokens**: ~8k  
**Status**: [ ] Not Started

## Objective

Ensure product MCP entry no longer depends on V2-shaped `DomainTools` mutators.

## Exact Steps

1. After WP4 session+evolve tools work, remove or `#if false` / delete unused V2 tool types from active discovery.
2. `Program.cs` / tool registration should only load V3 tools.
3. Grep Poly.Mcp for `Poly.Data.Modeling` — eliminate product references (or isolate dead code pending delete).
4. Update any README for Poly.Mcp.

## Verification

- [ ] Poly.Mcp builds
- [ ] Primary agent path uses V3 only
- [ ] Document residual V2 references if any (for WP8)

## Out of Scope

- Deleting Poly/Data/Modeling (WP8)
