# Plan: Domain Migration POC

**Date:** 2026-07-28  
**Goal:** Add the smallest working domain-level migration system so that storage-breaking domain changes can be declared, analyzed, and applied safely.

**Core principle:** Domain model remains the source of truth. Migrations are declarative, analyzed, and gated. Keep the first version tiny.

## Scope of the POC (in)

- Entity-scoped `migrate` blocks in the DSL
- Full Domain v1 vs Domain v2 diff analysis (not incremental)
- Structured breaking-change metadata
- Migration coverage check as a gate after normal analysis
- Simple domain version marker in the store
- Support only the most common transforms first:
  - Default value for new required / constrained properties
  - Simple property rename
  - Drop property
- Analysis rejects evolution when a breaking change has no covering migration
- Apply path: accept domain → run migrations on existing instances → update store version

## Explicitly out of scope for this POC

- Entity version tags on every instance
- Full historical migration chains / ordered multi-step history
- Reverse / down migrations
- Complex expression-based transforms
- Automatic EF Core migration generation
- Customer-facing git UI
- Multi-domain or cross-store migrations

## High-level design

1. **DSL**
   - New `migrate EntityName { ... }` blocks
   - First supported forms:
     ```poly
     migrate Book {
       PublishedYear = 0
       Title = OldTitle
       drop OldStatus
     }
     ```

2. **Analysis**
   - Keep existing single-domain analysis pipeline unchanged
   - Add new DomainDiff analysis that takes previous domain + new domain
   - Emits typed breaking-change records (added required property, tightened constraint, removed property, type change, etc.)
   - New coverage check: every breaking change must be covered by a migrate block

3. **Evolution gate order**
   1. New domain passes normal analysis
   2. DomainDiff runs against previous version
   3. Migration coverage is verified
   4. Only then accept and apply

4. **Runtime / Store**
   - Store holds a single current domain version (or domain fingerprint)
   - On accepted evolution: apply migrations to existing instances, then update the stored version

5. **Source of truth**
   - Domain DSL lives in git (already planned)
   - Store only tracks current version + live data

## Suggested task breakdown for agents

### Phase 0 – Foundations
- [ ] Define the `MigrateBlock` / migration intent shape in the domain model
- [ ] Extend the DSL parser to accept basic `migrate Entity { ... }` syntax
- [ ] Round-trip: parse → intents → print (export_dsl still works)

### Phase 1 – Diff & Breaking Changes
- [ ] Implement DomainDiff that compares two complete domains
- [ ] Produce structured change records (added/removed/changed properties, constraint changes, etc.)
- [ ] Classify which changes are storage-breaking
- [ ] Unit tests for common break cases (new required field, tightened length, removed property, type change)

### Phase 2 – Coverage Gate
- [ ] Implement migration coverage check against the breaking-change list
- [ ] Wire the check into the evolution path (after normal analysis, before accept)
- [ ] Clear diagnostic when a breaking change is uncovered
- [ ] Tests: evolution rejected without migration, accepted with matching migration

### Phase 3 – Runtime Apply
- [ ] Add domain version / fingerprint storage to the store (SQLite first)
- [ ] Implement the three basic transforms (default, rename, drop)
- [ ] Apply migrations to existing instances when evolution is accepted
- [ ] Update stored domain version after successful apply
- [ ] Tests with real instances that need transformation

### Phase 4 – Integration & Dogfood
- [ ] End-to-end: old domain + data → new domain + migrate blocks → analysis passes → data rewritten → new version stored
- [ ] Ensure export_dsl and normal MCP apply path still work
- [ ] Document the minimal supported migration syntax and the gate order

## Acceptance criteria for the POC

- A domain change that adds a required property without a migration is rejected with a clear error
- The same change with a matching `migrate` block that supplies a default is accepted
- Existing instances are rewritten correctly
- Store domain version is updated
- Normal (non-breaking) evolutions still work without migrations
- No regression in the existing 18-pass analysis or runtime tests

## Notes for agents

- Prefer the smallest change that makes the next test pass
- Do not invent general-purpose migration frameworks
- Keep migration syntax and domain shape syntax clearly separated
- Reuse existing analysis metadata where possible instead of re-walking
- All new diagnostics must be actionable for both humans and agents
