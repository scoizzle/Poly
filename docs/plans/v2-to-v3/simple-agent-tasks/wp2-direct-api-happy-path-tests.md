# Micro-Task: Direct API happy-path evolve tests

**Parent**: WP2  
**Difficulty**: Small–Medium  
**Estimated Tokens**: ~6k  
**Status**: [ ] Not Started

## Objective

Lock the M2 authoring path: bootstrap → entity → property → stage → action via `DomainEvolution.Evolve()`.

## Prerequisites

- Prefer WP1 factory/catalog if available; else minimal primitives inline.

## Exact Steps

1. Test file under `Poly.Tests/DomainModeling/` (e.g. `Direct/DomainAuthoringHappyPathTests.cs`).
2. Scenario: create domain with builtins; add entity `Order`; property; stage `Draft`; action `Submit`.
3. Assert success, not rolled back; query or inspect domain for members.
4. Second test: intentional failure (e.g. property on missing entity) → rolled back + diagnostics.

## Verification

- [ ] TUnit green
- [ ] Uses only V3 types

## Out of Scope

- MCP; policy eval; contract gen
