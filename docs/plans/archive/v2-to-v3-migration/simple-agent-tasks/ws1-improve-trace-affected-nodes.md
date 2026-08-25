# Micro-Task: Improve GetAffectedNodes + BuildTrace for Supported Changes

**Parent Workstream**: WS1 (Consolidated)  
**Difficulty**: Small  
**Estimated Context**: < 7k tokens

## Objective

Make `GetAffectedNodes` and `BuildTrace` return more useful data for the four MVP change types (instead of always empty). This makes traces immediately valuable for agents and unblocks better WS4 work.

## Required Reading

- Current `DomainEvolution.cs` (GetAffectedNodes + BuildTrace)
- The four DomainChange types
- EvolutionTrace / EvolutionStep shapes

## Exact Steps

1. Enhance `GetAffectedNodes` to yield relevant nodes (e.g. the Entity being added/removed/modified, or the Property).
2. Update `BuildTrace` to populate `AffectedNodeIds` from the nodes (using their `.Id.ToString()` or whatever the string form is).
3. Update step descriptions in the trace to be slightly more human-readable where trivial (optional).
4. Add or extend a test that checks the resulting `EvolutionTrace` has non-empty AffectedNodeIds after a real change.

## Verification

- Traces now contain affected node id information for the supported changes.
- Build + tests green.
- No over-engineering.

## Output

Better observable traces coming out of every `Apply` for the current MVP operations.

## Status

**Claimed by**:  
**Status**: Superseded (2026-07-10) — evolution foundation delivered; see master-roadmap.md
