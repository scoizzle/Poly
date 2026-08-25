# Domain Modeling — Technical Deep Dive

**Two subsystems:** V2 (`Poly/Data/Modeling/`, ~56,667 lines) and V3 (`Poly/DomainModeling/`, ~36,030 lines)

## Qualification Standard

Throughout this document, "dead code" means a value or method that is **computed or defined but the result is never consumed by any code path**. If the code is part of a coherent, self-contained system that produces a data model, and the unused parts are simply parts of that model that no current consumer queries, it is considered **dormant infrastructure** — not dead code — and is kept.

## Context

Per `docs/decisions/2026-05-31-immutable-core-domain-modeling.md`, V3 is the strategic target (immutable records, evolution layer), V2 is the production surface (mutable graph, MCP server). V2 has 43 external consumers (MCP, benchmarks, 32 test files). V3 has 4 external consumers (all test files).

No changes were made to the domain modeling subsystems. All review items were determined to be dormant infrastructure within coherent systems under active migration.

## Reviewed Items

### 3 unregistered V3 analyzers (~300 lines) — DELETED (Replaced)

**What they were:** `SemanticCoherenceAnalyzer.cs`, `IdempotencySafetyAnalyzer.cs`, `AuthoringSuggestionGenerator.cs` existed in `Poly/DomainModeling/Analysis/` but were never registered in `DomainModelAnalyzer.BuildPipeline()`.

**What replaced them:** `AuthoringSuggestionAnalyzer.cs` — a single, registered analyzer producing three kinds of authoring hints (missing stages, missing actions, missing policies). Wired into the pipeline at `UseDomainModelAnalysisPipeline()` after subscription analyzers. Uses `DomainModelDiagnosticCodes.AuthoringSuggestion` ("DMAS001") for its hints, exposed via MCP `get_domain_suggestions`.

### V3 Builders (9 files, ~600 lines) — KEEP

**What they are:** Fluent builder API for constructing V3 domain models.

**Why keep:** The builders ARE the primary construction API for V3. They're the equivalent of V2's `DomainMutationCommand` system, but for initial construction rather than mutation. Single consumer today because V3 itself has a single consumer — once V2 is migrated, they become the primary path.

### V2→V3 type gaps — ARCHITECTURAL, NOT SIMPLIFIABLE

`Actor`, `Rule` system, `ActionTrigger`, `EventSubscriptionAudience` exist in V2 but have no V3 equivalents. Porting them is additive work, not simplification.

### V3 evolution layer — DORMANT INFRASTRUCTURE

~2,000 lines, 66 `DomainChange` subtypes, working applicator. Zero production consumers. Part of the V3 data model under active Phase 1 development. Removing it now and rebuilding later is lost effort.

### 66 > 42 mutation tax — OBSERVATION

V3 has 66 `DomainChange` subtypes vs V2's 42 `DomainMutationIntent` subtypes. One of V3's stated goals was reducing mutation surface. This warrants architectural review but isn't a simplification opportunity — you can't remove V3 change types without breaking the evolution layer.
