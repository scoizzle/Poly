# Micro-Task: Domain query projections (direct API)

**Parent**: WP2  
**Difficulty**: Medium  
**Estimated Tokens**: ~8k  
**Status**: [x] **Done** — README uses `result.Root` (not `.Domain`); structured data records available for MCP consumption

## Objective

Model-optimized query projections over V3 `Domain` for the **direct library API** (tests + MCP both consume). No MCP types in core; no workspace type here.

## Exact Steps (original — largely done)

1. Add projections under e.g. `Poly/DomainModeling/Queries/`:
   - Overview: domain name, entity names/counts, event/relationship counts
   - Entity detail: properties (name+type name), stages, actions (names), policies (names)
   - Optional: analysis summary from `AnalysisResult` (error/warn counts + messages)
2. Pure static helpers or small `DomainQueries` type — natural names.
3. TUnit covering overview after evolve-add-entity.
4. Document one example in `DomainModeling/README.md`.

## Code-review follow-ups (do these before marking Done)

1. **README accuracy** — fix `result.Domain` → `result.Root` in `Poly/DomainModeling/README.md` Quick Start.
2. **Optional enrichment:** overview could include entity **name list** (not only counts) for agent/UI progressive disclosure — only if still concise.
3. Ensure MCP (WP4) can return **structured** projections (not only stringified `Message`) — coordinate with `wp4-mcp-session-and-overview` follow-ups; queries themselves may stay as records (already good).

## Verification

- [x] Build + query tests green (as of review)
- [x] No dependency on Poly.Mcp or Poly.Data.Modeling
- [ ] README uses `Root`
- [ ] Status → Done when README fixed (enrichment optional)

## Out of Scope

- Full export DTO tree; Mermaid; visual; diff revisions (later)
