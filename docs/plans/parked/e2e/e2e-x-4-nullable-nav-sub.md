# e2e-x-4 — Singular-nav subscription registration

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** x-3  
**Fleet:** P3-13 · Repro: `isolated-stagescoped.poly`

## Objective

`this.Node.Register…(this)` on `Node?` must not CS8602/NRE. Guard or link-time registration.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainToCSharpExporter.cs` (subscription register emit) | store runtime (e2e-s) |

## Status

**Status:** Not Started  
**Claimed by:**  
