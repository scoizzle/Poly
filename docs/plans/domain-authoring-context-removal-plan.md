# DomainAuthoringContext Removal Plan

Status: Draft
Owner: DomainModeling
Date: 2026-07-28

## Problem Statement

`DomainAuthoringContext` currently acts as a second configuration system that changes analyzer construction and behavior. This creates a second-system effect around analysis and violates the desired rule:

- There is exactly one domain analyzer system definition.
- Analysis behavior must not depend on an out-of-band mutable context object.

Current impact areas include:

- `DomainModelAnalyzer` overloads/build path branching by context.
- `DomainEvolution` accepting optional authoring context.
- MCP session state storing a global context.
- DslCompiler creating and threading authoring context for parse + analysis.
- Pack extensions mutating context (`AddSqliteDefaults`, `AddSqlServerDefaults`, `AddMySqlDefaults`).

## Target Architecture

One canonical analyzer pipeline definition with deterministic inputs:

1. `DomainModelAnalyzer` exposes one public analysis contract (plus incremental variant) with no authoring-context parameter.
2. Pack/vendor differences are represented as explicit domain artifacts/metadata inputs, not runtime pipeline wiring.
3. Parser feature toggles and analyzer behavior use the same explicit configuration model (no mutable session context singleton).
4. Evolution always analyzes with the same analyzer system definition.

## Pack Extensibility Model (Post-Context)

Packs remain powerful, but extension points move from mutable context wiring
to explicit product seams.

Design constraint:

- Packs compose on top of well-planned core APIs.
- Packs do not get a bespoke secondary subsystem for analysis/parsing behavior.
- If a pack needs a capability, that capability must exist as a first-class shared API seam.
- The Grammar system is the canonical DSL extension seam; pack syntax extensions must compose through it.

1. Custom nodes:
- Packs may define custom domain-model node types (new `Node`/domain member shapes).
- These nodes are authored into the canonical domain graph and participate in standard traversal.

2. DSL extensions:
- Packs may register parser/printer extensions that emit those custom nodes.
- Extension registration must be explicit and immutable for a compilation/session (no ambient mutable singleton).
- Grammar-based extensions are preferred/required over ad hoc parser side channels.
- Implementation track: align with `docs/plans/grammar-integration.md` so pack syntax
   extensibility lands through the shared grammar engine, not parser-specific hooks.

3. Custom analysis passes:
- Packs may contribute analyzers against the same single analyzer system definition.
- Contribution model is append-only through a declarative pass descriptor list, not pipeline branching.
- Pass ordering contracts must be explicit (dependencies/id), fail-closed on invalid order/duplicates.

4. Hard rule:
- Extensibility may add nodes/parsers/passes, but must not create an alternate analyzer system definition.
- There is one pipeline contract; packs contribute within it.

## Grammar Scope Boundary

Grammar/token-stream generalization decisions are tracked in the grammar plan,
not in this analyzer-unification plan:

- See docs/plans/grammar-integration.md (token model evolution, non-text stream support, and pack grammar extension sequencing).
- This plan remains focused on removing `DomainAuthoringContext` and enforcing one analyzer system definition.

## Scope

In scope:

- Remove `DomainAuthoringContext` from analyzer/evolution/MCP/DslCompiler call chains.
- Remove analyzer pipeline branch that depends on `DomainAuthoringContext`.
- Replace pack defaults with explicit, persisted configuration artifacts.
- Update tests/docs to enforce single analyzer definition.

Out of scope:

- Removing SQL annotation syntax support.
- Removing pack projects themselves.
- Redesigning non-analysis plugin APIs unrelated to domain analysis.

## Migration Strategy

### Phase A - Freeze and Guardrails

1. Mark `DomainAuthoringContext` as deprecated for analyzer/evolution use.
2. Add tests asserting one analyzer pass list for all analysis entry points.
3. Add fail-closed checks for any new authoring-context analyzer path.

Deliverables:

- Warnings on `DomainModelAnalyzer.Analyze(domain, DomainAuthoringContext?)` and related overloads.
- New tests in `Poly.Tests/DomainModeling/Analysis` enforcing single pipeline definition.

### Phase B - Introduce Explicit Analysis Inputs

1. Define immutable `DomainAnalysisInputs` (or equivalent) for analyzer-relevant knobs.
2. Move current pack-driven storage type/convention differences to explicit input values/materialized metadata.
3. Ensure inputs are serializable and can be persisted in MCP session/domain snapshots.
4. Add immutable `PackExtensionSet` descriptors for:
   - DSL reader/writer extensions
   - custom node factories/parsers
   - analyzer pass contributions

Rules:

- No pipeline mutation by pack extension methods.
- Inputs must be explicit in API signatures where needed.
- Pack contributions are validated before analysis starts; invalid extension sets fail loud.

### Phase C - Converge Analyzer API

1. Remove context-based pipeline builder branch from `DomainModelAnalyzer`.
2. Collapse to one analyzer construction path.
3. Keep incremental API, but with same pipeline definition.

Primary files:

- `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs`
- `Poly/DomainModeling/Analysis/StoragePass.cs` / `StorageAnalyzer.cs` (input consumption only)

### Phase D - Remove Evolution Context Dependency

1. Remove `DomainEvolution(..., DomainAuthoringContext?)` constructor option.
2. Remove context field/storage from evolution path.
3. Ensure evolution analysis always uses single analyzer definition + explicit inputs.

Primary files:

- `Poly/DomainModeling/Evolution/DomainEvolution.cs`

### Phase E - Remove MCP Session Context Singleton

1. Delete `McpSessionStore.Context` mutable singleton usage.
2. Persist explicit analysis/parser inputs in session state instead.
3. Update MCP tools that currently read/write context.

Primary files:

- `Poly.Mcp/Sessions/McpSessionStore.cs`
- `Poly.Mcp/Tools/*`

### Phase F - DslCompiler + Packs Migration

1. Replace `CreateAuthoring(DbmsPack)` with explicit `DomainAnalysisInputs` / `ParserInputs` creation.
2. Refactor pack extension APIs from mutating authoring context to producing immutable input descriptors (`PackExtensionSet`).
3. Keep command surface (`DbmsPack`) but map to explicit inputs, not analyzer pipeline mutation.

Primary files:

- `src/Poly.DslCompiler/DslCompiler.cs`
- `src/Poly.Packs.Sqlite/SqliteDefaults.cs`
- `src/Poly.Packs.SqlServer/SqlServerDefaults.cs`
- `src/Poly.Packs.MySql/MySqlDefaults.cs`

### Phase G - Final Removal

1. Delete `Poly/DomainModeling/DomainAuthoringContext.cs`.
2. Delete old overloads and compatibility shims.
3. Remove/replace all tests that instantiate `DomainAuthoringContext`.

## Breaking Changes and Compatibility Plan

Breaking surfaces:

- `DomainModelAnalyzer.Analyze(domain, DomainAuthoringContext?)`
- `DomainModelAnalyzer.Analyze(domain, DomainAuthoringContext?, priorAnalysis, invalidatedNodes)`
- `DomainEvolution(Domain, DomainAuthoringContext?)`
- `PolyDslParser(string, DomainAuthoringContext?)` (if parser context is also removed/replaced)

Compatibility approach:

1. One release deprecation window with compile warnings.
2. Migration helpers that map old pack selections to new immutable inputs.
3. Remove deprecated APIs after test suite + docs migration.

## Verification Matrix

Required checks per phase:

1. `dotnet run --project Poly.Tests/Poly.Tests.csproj`
2. `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
3. Search checks:
   - No new `DomainAuthoringContext` references in analysis/evolution paths.
   - Single analyzer definition in `DomainModelAnalyzer`.

Final acceptance criteria:

- Exactly one analyzer pipeline definition exists.
- No analyzer behavior branch based on authoring context.
- Evolution/MCP/DslCompiler do not require mutable authoring context for analysis.
- Existing pack behavior is preserved via explicit immutable inputs.

## Risk Register

1. Pack behavior drift (storage types/conventions change unexpectedly).
- Mitigation: golden tests for Sqlite/SqlServer/MySql outputs before and after migration.

2. MCP session backward compatibility.
- Mitigation: session schema migration that maps old context-derived state to new inputs.

3. Incremental analysis cache invalidation differences.
- Mitigation: explicit input fingerprinting in analysis cache keys.

4. Pack extension ordering/compatibility faults.
- Mitigation: deterministic extension graph validation + startup diagnostics before first parse/analyze.

## Suggested Task Breakdown

1. A1: Deprecation attributes + single-pipeline guard tests.
2. B1: Introduce immutable analysis input model + mapping from existing pack options.
3. C1: Remove context branch from `DomainModelAnalyzer`.
4. D1: Remove evolution context dependency.
5. E1: Replace MCP session context with explicit session inputs.
6. F1: Refactor DslCompiler and pack defaults to immutable inputs.
7. G1: Delete `DomainAuthoringContext` + cleanup + docs/ADR updates.

## Documentation Updates Required During Execution

1. Update `docs/CORE.md` to codify single analyzer system definition and explicit input model.
2. Add ADR recording why `DomainAuthoringContext` was removed.
3. Update DAU-era plan references that still describe context-driven analysis.
