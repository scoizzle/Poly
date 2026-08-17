# DomainModeling — combined cleanup implementation (executable)

**Date:** 2026-08-17  
**Combines:** [`domainmodeling-vision-cleanup-2026-08-16.md`](domainmodeling-vision-cleanup-2026-08-16.md) (the executable deletion of dual paths) and [`domainmodeling-target-architecture-2026-08-16.md`](domainmodeling-target-architecture-2026-08-16.md) (the end-state layout).  
**Status:** Execution checklist. The three cleanup slices are the only thing admitted here; folder renames and M1/M3/M4 wait on host-ABI `CallExternal` and are **not** part of this plan.

---

## What "combine" means

The target-architecture doc answers *where each phase lives*; the cleanup doc answers *what to delete, in what order, with which failing test*. The mapping (target §9) is the merge:

| Cleanup slice | Target move | Result |
|---------------|-------------|--------|
| Slice 1 — One door | **M2** (session owns the phases) + **M6** (libraries are the only place meaning differs) | `Domain` / `ExtensionCatalog` / `DomainSession` are the only three nouns; `DomainHost*` gone |
| Slice 2 — Analyze sees the session | **M2** + **M5** (single-purpose analysis, core-seed pipeline at `Open`) | `session.Analyze` is the only analysis door; no static `DomainModelAnalyzer.Analyze`, no compiler re-run of `StoragePass` |
| Slice 3 — Comment is not meaning | honesty bar (target invariant 3) | emit fails closed; `ExecuteStructured` stays as the *named* runtime seam |

**Not admitted here (target §3, "still a lie"):** M1 (finish `ToSyntax` split-walk, move C# idiom out), M3 (one lowering shape), M4 (runtime shrinks to harness), and every folder rename in target §2/§4. Those become executable only after host-ABI `CallExternal` lowering exists.

---

## Execution order (TDD, build green at each step)

Each slice follows the cleanup doc's loop: **first failing test → smallest change → green → re-review → only then next slice.**

### Slice 1 — One door ✅ DONE 2026-08-17

**Invariant:** Unknown `uses` throws. The only assembler is `DomainSession` from the catalog.

**Done (verified green, 2205 tests):**
1. `DomainSession.Open` / `ForSource` / `ForExtensions` throw on unknown id (no `failOnUnknown`).
2. Folded `DomainHost`, `DomainHostBuilder`, `DomainParserInputs`, `DomainAnalysisInputs` into `DomainSession` + a new public `SessionBuilder` (same `CreateEmpty`/`Load`/`Build` shape as the old builder; `Build()` returns `DomainSession`). `IDomainLibrary.Register(SessionBuilder)`.
3. `ExtensionCatalog.ResolveHost` deleted; `Language`/`Authoring` now return `DomainSession`.
4. `DomainCompilation.HostForSource` → `DomainSession.ForSource`.
5. Deleted `DomainSession.FromInputs`; `PolyDslParser`/`DomainDslPrinter`/`DslExpressionFragment` take a session.
6. `DslGrammar.For` / `LanguageFor` take `(AnnotationRegistry, ExpressionFormRegistry)`.

**Stop rg verified empty in product source:** `ResolveHost`, `DomainHostBuilder`, `DomainParserInputs`, `DomainAnalysisInputs`, `DomainHost`, `FromInputs`, `failOnUnknown`.

**Still a lie (documented in code):** `CreateInputs` is a third assembler (defer to slice 2); `DomainEvolution.Apply` still calls the static `DomainModelAnalyzer.Analyze`. Two analysis-time helpers reopen sessions from a bare domain and must degrade gracefully now that `Open` fails closed:
- `ExpressionMeaning.For(domain)` filters to `ExtensionCatalog.Core.Contains(id)` (vendor ids contribute no meaning).
- `StoragePass` catches `InvalidOperationException` on its `DomainSession.Open(domain)` fallback (vendor ids have no maps in the core catalog).

These two are exactly the slice-2 seam.

### Slice 2 — Analyze sees the session ✅ DONE 2026-08-17

**Invariant:** Authoring, MCP, and compile analyze through `session.Analyze`, which constructs `StoragePass` with the session's `TypeMaps` + `StorageConventions`.

**Done (verified green, 2205 tests):**
1. `DomainSession.Analyze(Domain)` + `Analyze(Domain, priorAnalysis, invalidatedNodes)` — lazily build the session's `Analyzer` via a new `DomainModelAnalyzer.BuildPipeline(typeMaps, conventions)` factory that wires the session maps into `StoragePass`.
2. `UseDomainModelAnalysisPipeline(typeMaps, conventions)` now parameterizes `StoragePass`.
3. `DomainEvolution.Apply(changes, priorAnalysis, session)` — `ResolveSession(domain, session)` = `session ?? DomainSession.ForExtensions(domain.Extensions)`; the compiler threads its `parseSession` (carries `CompilerCatalog` maps).
4. `McpSessionStore.Create` → `modeling.Analyze(domain)`. `Evolve` passes `current.Modeling` into `EvolutionBuilder.Apply`. `apply_dsl` reuses the parse session for `DomainEvolution.Apply`.
5. `DslCompiler`: deleted the `needsInfraPipeline` `StoragePass` re-run; `GenerateAllFiles` reads `StorageMappingMetadata` straight from the session analysis.

**Stop rg verified:** `DomainModelAnalyzer.Analyze` gone from `DomainEvolution`/`McpSessionStore`; `new StoragePass(` gone from `DslCompiler.cs`.

**Still a lie (out of slice-2 scope per plan):** `BehaviorMetadata.BuildBehavior` and `RuntimeAnalysisCache.GetOrAnalyze` still call the static `DomainModelAnalyzer.Analyze` (runtime/emit helpers, not authoring/MCP/compile; not in the stop-rg). `CreateInputs` + session-based `Compile` surface remain (the "third assembler").

### Slice 3 — Honest lowering ✅ DONE 2026-08-17

**Invariant:** Unlowerable effects are errors on **emit**; `Comment` is not shipped meaning; `ExecuteStructured` stays the named runtime seam.

**Done (verified green, 2205 tests):**
1. `EffectLoweringPass.Composite`/`Conditional`: a sub-effect that fails `Route` now throws (fail-closed) instead of emitting a `Comment` placeholder.
2. `DomainToCSharpExporter`: removed all `new Comment(...)` sites — subscription handler empty body → `new Block([])`, null-composite fallback → throw, EF materialization ctor → empty block, empty action body → empty block, adapter holder ctor → empty block.
3. `ExecuteStructured` + `EffectExecutor` retained as the named runtime seam.

**Stop rg verified:** `new Comment(` empty across `Poly/`, `src/`, `Poly.Mcp/`; `ExecuteStructured` still present in `DomainEntityInstance.cs`.

**Still a lie (unchanged, carried forward):** runtime and emit are **not** the same trees for store effects; `LowerStageTransitions` (emit-mode) still exists; "one lowering" is not locally true for `transition`/`create`/`create in`/`invoke`/`for invoke`. These are the next suite's problem (host-ABI `CallExternal`), not this plan.

---

## Acceptance

- Build green, `Poly.Tests` suite green after each slice.
- `git diff --stat HEAD` clean of the deleted nouns in product trees.
- Pre-ship review gate run before any slice is marked done (per AGENTS.md).

## Honesty (carry forward)

After slice 3 the tree still does **not** have "one lowering" for store effects, `CreateInputs`, `Program.cs` without `uses http`, or three MCP simulate shapes. Those are documented in the cleanup plan's "still a lie" table and are the next suite's problem, not this one.
