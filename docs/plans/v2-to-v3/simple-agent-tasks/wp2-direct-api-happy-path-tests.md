# Micro-Task: Direct API happy-path evolve tests

**Parent**: WP2  
**Difficulty**: Small–Medium  
**Estimated Tokens**: ~6k  
**Status**: [x] **Done** — failure tests use reliable structural failures (duplicate entity, unknown parent stage); silent-no-op documented in WP3

## Objective

Lock the M2 authoring path: bootstrap → entity → property → stage → action via `DomainEvolution.Evolve()`.

## Prerequisites

- Prefer WP1 factory/catalog if available; else minimal primitives inline.

## Exact Steps (original — largely done)

1. Test file under `Poly.Tests/DomainModeling/` (e.g. `Direct/DomainAuthoringHappyPathTests.cs`).
2. Scenario: create domain with builtins; add entity `Order`; property; stage `Draft`; action `Submit`.
3. Assert success, not rolled back; query or inspect domain for members.
4. Second test: intentional failure (e.g. property on missing entity) → rolled back + diagnostics.

## Code-review follow-ups (do these before marking Done)

1. **Real silent-no-op coverage** — missing-entity property add currently **no-ops** at mutation time and may not roll back. Either:
   - assert current behavior explicitly **and** file a DomainChange/analyzer gap, **or**
   - after WP1/evolution fix makes missing targets fail analysis, assert `WasRolledBack` + diagnostics.
2. Align any failure tests with **WP1 factory** follow-ups (duplicate entity is a reliable structural failure).
3. Keep tests on **V3 only** (no MCP required for this task).

## Verification

- [x] Happy-path tests green (as of review)
- [ ] Failure/no-op follow-up addressed
- [ ] Uses only V3 types

## Out of Scope

- MCP; policy eval; contract gen
