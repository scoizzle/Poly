# Micro-Task: V2 delete gate check (pre-WP8)

**Parent**: WP8  
**Difficulty**: Small  
**Estimated Tokens**: ~4k  
**Status**: **Superseded** — `Poly/Data/Modeling` already removed; treat M4 as Done (commit purge if still unstaged)  
**Depends on**: —

## Objective

Prove whether `Poly/Data/Modeling` is **safe to delete** or list remaining blockers.

## Exact Steps

1. Grep solution for `Poly.Data.Modeling` / `Poly/Data/Modeling` (exclude `docs/`, archive, comments if possible).
2. Classify remaining hits: tests, demos, dead MCP DomainTools, core.
3. Write `docs/plans/v2-to-v3/spikes/v2-delete-readiness.md`:
   - **Ready to delete** or **Not ready** with file list
   - Suggested delete order if ready
4. Do **not** delete yet unless inventory shows zero product/test value left **and** human/orchestrator already approved delete in same session.

## Verification

- [ ] Readiness file exists
- [ ] Explicit Ready / Not ready

## Out of Scope

- Performing the delete unless explicitly unblocked and trivial
