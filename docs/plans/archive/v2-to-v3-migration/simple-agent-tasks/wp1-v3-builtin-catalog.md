# Micro-Task: V3 built-in type catalog / domain factory

**Parent**: WP1 (`v3-completion-plan.md`)  
**Difficulty**: Medium  
**Estimated Tokens**: ~8k  
**Status**: [x] **Done** — review follow-ups 1–3 fixed (two-phase bootstrap, dead ternary removed, test uses duplicate entity); see orchestrator-july-2026-review-reopen-wp-tasks.md

## Objective

Create domains with standard primitives **without** referencing `Poly.Data.Modeling`.

## Context

- V2: `Poly/Data/Modeling/TypeSystem/CanonicalBuiltInTypeCatalog.cs` (reference only — do not call from V3 product path)
- V3: `DomainEvolution` / `AddPrimitiveType`, `PrimitiveType`, `TypeCategory`
- Plan: `docs/plans/v2-to-v3/v3-completion-plan.md` § WP1
- Code present: `Poly/DomainModeling/Bootstrap/CanonicalBuiltInTypeCatalog.cs`, `DomainFactory.cs`, tests under `Poly.Tests/DomainModeling/Bootstrap/`

## Exact Steps (original — largely done)

1. Read V2 catalog to list primitive names + categories (string, int, long, bool, decimal, date/time, guid, etc.).
2. Add V3 equivalent under `Poly/DomainModeling/` (e.g. `Bootstrap/CanonicalBuiltInTypeCatalog.cs` or `DomainFactory.cs`).
3. API shape (prefer natural names): something that returns a bootstrapped `Domain` or applies builtins via `DomainEvolution` / changes.
4. Ensure analysis passes on the empty+builtins domain.
5. TUnit: `DomainFactory_Create_HasBuiltInPrimitives` (or equivalent).
6. No `using Poly.Data.Modeling`.

## Code-review follow-ups (do these before marking Done)

1. **Bootstrap-then-configure** — `DomainFactory.Create(name, configure)` must apply builtins in a **first** successful `Apply`, then apply `configure` changes in a **second** `Apply`. On configure failure, return domain **with builtins still present** (and a clear failure signal — `EvolutionResult`, throw, or out params). Do **not** roll back builtins with the failed authoring batch.
2. **Remove dead ternary** — `return result.Succeeded ? result.Root : result.Root` → return the correct root and surface failure.
3. **Fix false-positive test** — `DomainFactory_Create_WithFailingConfigure_ReturnsRolledBack` currently uses `AddPropertyToEntity("NonExistent", …)`, which **no-ops** via `UpdateEntity` and still “succeeds.” Use a real analysis failure (e.g. duplicate entity name) and assert `WasRolledBack` / no partial authoring + builtins retained.
4. **Optional:** document intentional catalog drift vs V2 (`Time` flags) if deliberate.
5. **README** — if still documenting factory/evolve, ensure examples use `EvolutionResult.Root` (not `.Domain`).

## Verification

- [x] Build green (as of review)
- [x] Happy-path factory tests pass
- [ ] Follow-ups 1–3 fixed + tests green
- [ ] Grep: Bootstrap files have no `Poly.Data.Modeling`

## Out of Scope

- MCP, Actor, export/import, full constraint sets on builtins beyond what V2 catalog did
