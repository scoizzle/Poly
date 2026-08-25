# Micro-Task: Retire V2 DomainTools product path

**Parent**: WP4  
**Difficulty**: Medium  
**Estimated Tokens**: ~8k  
**Status**: [x] **Done** — Program.cs registers V3 tools only (re-verified after evolve-tool edits); DomainTools.cs has deprecation header; Poly.Mcp README documents V3 active path and V2 deprecated status

## Objective

Ensure product MCP entry no longer depends on V2-shaped `DomainTools` mutators.

## Exact Steps (original — largely done)

1. After WP4 session+evolve tools work, remove or `#if false` / delete unused V2 tool types from active discovery.
2. `Program.cs` / tool registration should only load V3 tools.
3. Grep Poly.Mcp for `Poly.Data.Modeling` — eliminate product references (or isolate dead code pending delete).
4. Update any README for Poly.Mcp.

## Code-review follow-ups (do these before marking Done)

1. **Confirm `Program.cs`** only registers V3 tool types (already true as of review — re-verify after evolve-tool edits).
2. **Grep** `Poly.Mcp` for live V2 usage on the product path (assembly may still reference V2 via dead `DomainTools.cs` — acceptable until WP8 if **not registered** and clearly deprecated).
3. **Optional for Done:** short `Poly.Mcp` README note: V3 tools entrypoint + “V2 DomainTools deprecated / not registered.”
4. **Do not** full-delete `Poly/Data/Modeling` here (WP8).

## Verification

- [x] Poly.Mcp builds; V3-only registration (as of review)
- [ ] Re-verify after WP4 follow-ups
- [ ] Deprecation documented (optional but preferred)
- [ ] Residual V2 file inventory noted for WP8

## Out of Scope

- Deleting Poly/Data/Modeling (WP8)
