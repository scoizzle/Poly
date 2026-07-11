# Micro-Task: Declare V2 freeze (WP6)

**Parent**: WP6 (`v3-completion-plan.md`)  
**Difficulty**: Small (docs + grep inventory)  
**Estimated Tokens**: ~4k  
**Status**: [ ] Not Started

## Objective

Formally freeze `Poly/Data/Modeling` (V2): no new features; only build-breaking fixes allowed until deletion (WP8).

## Context

- V3 MCP + direct API path is the product path (`Poly.Mcp` V3 tools only).
- V2 still lives in-tree for residual tests/demos until WP7/WP8.
- Decisions: `docs/decisions/2026-v2-to-v3-domain-modeling-port.md`
- Roadmap: `docs/plans/v2-to-v3/master-roadmap.md` M3

## Exact Steps

1. Add a short **V2 Freeze** subsection to `docs/plans/v2-to-v3/master-roadmap.md` (date, rule: no new V2 features).
2. Add a one-line freeze note to `docs/decisions/2026-v2-to-v3-domain-modeling-port.md` status snapshot.
3. If `AGENTS.md` has a DomainModeling / placement section, add: **Do not add features to `Poly/Data/Modeling`.** Route work to `Poly/DomainModeling`.
4. Grep inventory (paste into agent-summary or a bullet list in the roadmap freeze section):
   - Count of `using Poly.Data.Modeling` under `Poly.Tests`, `Poly.Benchmarks`, `Poly.Mcp` (note DomainTools.cs is dead registration)
   - Top directories still referencing V2
5. Update readiness checklist: **V2 frozen** → ✅

## Verification

- [ ] Freeze text visible in master roadmap + decision status
- [ ] AGENTS.md (or equivalent) warns agents off V2 features
- [ ] Inventory exists for WP7 to consume
- [ ] No code behavior change required (docs-only OK)

## Out of Scope

- Deleting V2 code
- Porting tests (WP7)
- MCP changes
