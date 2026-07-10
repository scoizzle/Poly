# Micro-Task: V3 built-in type catalog / domain factory

**Parent**: WP1 (`v3-completion-plan.md`)  
**Difficulty**: Medium  
**Estimated Tokens**: ~8k  
**Status**: [ ] Not Started

## Objective

Create domains with standard primitives **without** referencing `Poly.Data.Modeling`.

## Context

- V2: `Poly/Data/Modeling/TypeSystem/CanonicalBuiltInTypeCatalog.cs` (reference only — do not call from V3 product path)
- V3: `DomainEvolution` / `AddPrimitiveType`, `PrimitiveType`, `TypeCategory`
- Plan: `docs/plans/v2-to-v3/v3-completion-plan.md` § WP1

## Exact Steps

1. Read V2 catalog to list primitive names + categories (string, int, long, bool, decimal, double, DateTime, Guid, etc.).
2. Add V3 equivalent under `Poly/DomainModeling/` (e.g. `Bootstrap/CanonicalBuiltInTypeCatalog.cs` or `DomainFactory.cs`).
3. API shape (prefer natural names): something that returns a bootstrapped `Domain` or applies builtins via `DomainEvolution` / changes.
4. Ensure analysis passes on the empty+builtins domain.
5. TUnit: `DomainFactory_Create_HasBuiltInPrimitives` (or equivalent).
6. No `using Poly.Data.Modeling`.

## Verification

- [ ] Build green
- [ ] New test passes
- [ ] Grep: new files have no `Poly.Data.Modeling`

## Out of Scope

- MCP, Actor, export/import, full constraint sets on builtins beyond what V2 catalog did
