# Micro-Task: Domain query projections (direct API)

**Parent**: WP2  
**Difficulty**: Medium  
**Estimated Tokens**: ~8k  
**Status**: [ ] Not Started

## Objective

Name-first, concise query projections over V3 `Domain` for tests and MCP (no MCP types in core).

## Exact Steps

1. Add projections under e.g. `Poly/DomainModeling/Queries/`:
   - Overview: domain name, entity names/counts, event/relationship counts
   - Entity detail: properties (name+type name), stages, actions (names), policies (names)
   - Optional: analysis summary from `AnalysisResult` (error/warn counts + messages)
2. Pure static helpers or small `DomainQueries` type — natural names.
3. TUnit covering overview after evolve-add-entity.
4. Document one example in `DomainModeling/README.md`.

## Verification

- [ ] Build + tests green
- [ ] No dependency on Poly.Mcp or Poly.Data.Modeling

## Out of Scope

- Full export DTO tree; Mermaid; visual; diff revisions (later)
