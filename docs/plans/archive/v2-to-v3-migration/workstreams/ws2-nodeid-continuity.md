# Workstream WS2: NodeId Continuity Strategy & Implementation

> **Superseded (June 2026):** This workstream file is historical.
>
> NodeId continuity scope is tracked in `ws1-evolution-applicator-mvp.md`.

> **Archive note:** Status/owner fields below reflect the original draft state and are not current execution truth.

**Phase**: 1  
**Priority**: High (enables good incremental analysis)  
**Owner**: Historical draft (superseded)  
**Status**: Superseded  
**Last Updated**: 2026-06 (superseded)

## Goal
Define and implement a reliable strategy for preserving stable `Node.Id` values when producing new immutable `Domain` roots from previous ones. This is critical for incremental analysis performance and for analyzers that rely on stable identity across versions.

## Entry Criteria
- WS1 has defined the basic shape of how new roots are produced (even if early).
- Relevant decisions reviewed.

## Key Tasks

1. **Design the strategy**
   - Options: copy-on-write style id preservation, structural diff + id mapping, explicit "stable id" construction helpers in builders, post-processing pass, etc.
   - Document trade-offs (complexity vs. analysis quality vs. allocation cost).

2. **Implement the chosen approach**
   - Integrate into the evolution applicator (coordinate closely with WS1 owner).
   - Handle creation of new nodes (new ids) vs. preserved nodes (copied ids).

3. **Update V3 builders / construction paths** (if needed)
   - Add optional support for "inherit id from previous version" in key builders.

4. **Testing & validation**
   - Prove that unchanged subtrees retain their ids across evolutions.
   - Prove that V3 analyzers (structural, semantic, etc.) continue to work and can take advantage of incrementality.
   - Measure / demonstrate reduction in re-analysis cost.

5. **Decision record**
   - Create a proper decision record documenting the chosen strategy, rationale, and known limitations.

## Exit Criteria
- Stable NodeId behavior across evolutions is implemented and tested.
- Decision record exists and is referenced from the master roadmap.
- Clear documentation for future agents on how ids behave during evolution.
- No breakage to existing V3 analysis passes.

## Dependencies
- WS1 (needs the applicator to exist in some form)
- Can start design work in parallel with early WS1 skeleton.

## Parallelism Notes
This workstream has a natural handoff point with WS1. The design phase can proceed somewhat independently, but implementation must be tightly coordinated with the owner of WS1.

## Related Documents
- `docs/decisions/2026-05-31-evolution-layer-design.md` (mentions NodeId continuity as a key open question)
- Future decision record for this strategy (to be created by this workstream)

## Current Open Questions
- What is the minimal viable strategy that still gives us good incremental analysis wins?
- How do we handle nodes that are "logically the same" but have been structurally modified in small ways?