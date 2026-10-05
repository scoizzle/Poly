# e2e-x-11 — create binding `default(now/today/guid)`

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** x-10  
**Fleet:** P3-20 · Repro: `12-mcp/mcp-create-defaults-fail.poly`

## Objective

`AppendDefaultedPropArgs` must not call `LowerDefaultConstantNode` on runtime keywords. Guard that branch.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainToCSharpExporter.cs` (`AppendDefaultedPropArgs` / default lower) | |

## Status

**Status:** Not Started  
**Claimed by:**  
