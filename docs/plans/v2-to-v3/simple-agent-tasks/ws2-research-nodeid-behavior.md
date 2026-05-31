# Micro-Task: Research and Document NodeId Behavior in V3

**Parent Workstream**: WS2 - NodeId Continuity Strategy & Implementation  
**Difficulty**: Small Model Friendly (research + documentation)  
**Estimated Context**: Low

## Objective
Produce a clear, concise document explaining how `Node.Id` and `NodeId` currently work in the V3 immutable model and the shared Syntax analysis infrastructure.

## Context You Must Read First

- `Poly/Syntax/Node.cs` and `NodeId.cs`
- `Poly/DomainModeling/DomainObject.cs` and `DomainMember.cs` (how they inherit from Node)
- Any existing usage of NodeId in V3 analyzers or the Evolution design docs

## Exact Steps

1. Trace how a new `Domain` (and its children) gets `NodeId` values assigned.

2. Understand the difference between structural identity and `Node.Id`.

3. Document:
   - When new Ids are generated
   - How Ids are used for metadata / incremental analysis today
   - Any existing stability guarantees (or lack thereof) when creating new versions of objects

4. Save the findings as a short document (e.g. in `spikes/` or attached to WS2).

## Verification

- [ ] The research document is clear and saved
- [ ] It correctly identifies current behavior vs. what WS2 will need to preserve
- [ ] It is referenced from the WS2 workstream file

## Output

A short research note / document that future work on NodeId continuity can rely on.

## Status

**Claimed by**:  
**Status**: Not Started / In Progress / Done (summary submitted)