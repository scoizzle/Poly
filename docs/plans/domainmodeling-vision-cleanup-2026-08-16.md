# DomainModeling — vision cleanup (three slices)

**Date:** 2026-08-16  
**Status:** Proposal. **Not CURRENT.** Do not execute until admitted (`simple-agent-tasks/PIPELINE-STATUS.md`). This file does not admit itself.  
**Supersedes:** [`domainmodeling-vision-cleanup-2026-08-15.md`](domainmodeling-vision-cleanup-2026-08-15.md) (five-wave story; review found its ACs self-contradictory).  
**Lock:** [`docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](../decisions/2026-08-15-domain-library-extensions-mcp-harness.md) · [`2026-08-14-domain-libraries.md`](../decisions/2026-08-14-domain-libraries.md) · `docs/CORE.md` · AGENTS platform facts.  
**Review that forced this rewrite:** [`docs/agent/reviews/2026-08-15-vision-cleanup-plan-review.md`](../agent/reviews/2026-08-15-vision-cleanup-plan-review.md).

The spine does not change: `.poly → Domain → session load → analyze → lower → export/VM`.  
No Grammar rewrite. No VM completeness. No new IR, coordinator, MEF, or 12-method plugin.

---

## Target (do not reopen)

```text
Domain           = facts (types, navs, contracts, uses ids). Not a process. No Main.
ExtensionCatalog = id → IDomainLibrary
DomainSession    = the compile
Lowering         = one complete Syntax AST per shipped operation
Interpretation   = runs / prints that AST
Extensions       = opt-in (uses). REST is a door, not core.
Poly.MCP         = harness: author, inspect, simulate with supplied context
```

This plan does **not** make every lock locally true. After the last slice, write down what is still a lie.

---

## Honesty (recomputed 2026-08-16)

| Lock | Code |
|------|------|
| Unknown `uses` fails closed | `DomainSession.Create` → `ResolveHost(ids, failOnUnknown: false)` skips unknown ids (`DomainSession.cs:92-93`). Catalog default throws (`ExtensionCatalog.cs:64-77`). |
| One load path | Three: catalog `ResolveHost`; `DslCompiler.CreateInputs` always `Load(Temporal+Storage+vendor)` + optional MinimalAPI contributor; `ExtraArtifactLibrary` Guid ids (`DslCompiler.cs:232-280`). Parse uses `HostForSource` (`:147`). |
| Analyze is the session | `DomainEvolution.Apply` and `McpSessionStore` call static `DomainModelAnalyzer.Analyze` (`DomainEvolution.cs:38-39,118`; `McpSessionStore.cs:59`). Pipeline `StoragePass()` has **no** type maps (`DomainModelAnalyzer.cs:87`). Compiler re-runs `StoragePass` when maps/conventions exist (`DslCompiler.cs:376-397`) — that is how Sqlite `TEXT`/`INTEGER` land. |
| One lowering | Emit-mode context: `LowerStageTransitions` also gates invoke / for-invoke / create / create-in (`EffectLoweringPass.cs:216+`). `UseThisReference`, `PostTransitionNodes`, `SourceStageName`, nav resolvers change **which nodes** exist (`LoweringContext.cs:82-88`). |
| No Comment / second interpreter as meaning | 8 `new Comment(` sites. Mixed `if`+create **must** use `ExecuteStructured` (`DomainEntityInstance.cs:594-611`) because children become Comment (`EffectLoweringPass.cs:406-416`). |
| MCP simulate = name + context + same AST | `simulate_policy` = no session, fake `Entity("Subject")`, `InferPropertyTypes` (`OracleTool.cs:370-415`). `evaluate_policy` = named policy. `create_instance`+`invoke_action` = store. |
| Core has no `Program.cs` | `CompileMode.All` adds `MinimalApiHostArtifactContributor`. |

`IDomainPack` and `Domain.ResolveHost` are already gone. Do not put them on a kill list.

---

## Slices (1 → 2 → 3, park between)

Each slice: one invariant, kill list with **call sites**, what becomes true, **what remains a lie**, the next test that is still red on purpose.

Do not write “byte-identical,” “behaviors unchanged,” or “identical C#.” Write the intended delta.

### Slice 1 — One door

**Invariant:** Unknown `uses` throws. The only assembler is `DomainSession` from the catalog.

**First failing test:** `DomainSession.Open` on a domain with `uses nope` throws. `rg failOnUnknown: false` empty in `Poly/` and `src/`.

**Kill / fold**
- `ResolveHost(..., failOnUnknown: false)` on `DomainSession.Create`. Delete the `false` overload on the session path.
- `DomainHost` / `DomainHostBuilder` / `DomainParserInputs` / `DomainAnalysisInputs` → fields on `DomainSession` (or a private `SessionBuilder`). `IDomainLibrary.Register` takes that builder.
- `ExtensionCatalog.ResolveHost` → builds a session (or `Resolve(id)` + `Open`).
- `DomainCompilation.HostForSource` → `DomainSession.ForSource`.
- `DomainSession.FromInputs` (rebuilds Temporal via a side door).

**Call sites:** `DomainSession.cs`, `DomainHost.cs`, `Packs/ExtensionCatalog.cs`, `Packs/DomainCompilation.cs`, `Packs/IDomainLibrary.cs`, `DslCompiler.cs:147` (`HostForSource`), tests that call `ResolveHost` / `FromInputs` / `CreateEmpty`.

**Not this slice:** `DbmsPack` enum, `--pack`, `CreateInputs`, `GenerateAllFiles`, `Packs/` rename, MySQL as CLI seed (it is not in `CompilerCatalog`). `--dbms sqlite` may still seed id `sqlite`. Folding `*Defaults` into `*Library.Register` is allowed if it is a move with no CLI story change.

**Becomes true:** `uses nope` cannot open. Product parse can go through `ForSource`. Catalog and session tell the same story.

**Still a lie:** `CreateInputs` is a third assembler. `DomainEvolution` still calls `DomainModelAnalyzer`. `failOnUnknown` may still exist on the catalog helper if unused.

**Next red on purpose:** `DomainEvolution.Apply` ignores session passes.

**Stop rg (product `Poly/`, `src/`, `Poly.Mcp/` only):** `ResolveHost`, `DomainHostBuilder`, `FromInputs`, `failOnUnknown: false`.

---

### Slice 2 — Analyze sees the session

**Invariant:** Authoring, MCP, and compile analyze through `session.Analyze`. That analyze constructs `StoragePass` with the session’s `TypeMaps` and `StorageConventions`.

**First failing test:** Sqlite fixture column types (`Text`→`TEXT`, …) appear in DbContext from `session.Analyze` metadata. Compiler must not construct a second `StoragePass` to get them.

**Kill / fold**
- Product `DomainModelAnalyzer.Analyze` call sites: `DomainEvolution.Apply` (`:38-39`, `:118`), `McpSessionStore` (`:59`), any MCP tool that re-analyzes. Static type becomes `session.Analyze` or a thin wrapper that **requires** a session.
- `UseDomainModelAnalysisPipeline` is the core-seed pass list registered at `Open`. Libraries `AddPass`; duplicate name fails closed.
- `GenerateAllFiles` `needsInfraPipeline` re-run — **only after** the session `StoragePass` has the maps. Then delete the re-run.

**Call sites:** `DomainModelAnalyzer.cs`, `DomainEvolution.cs`, `McpSessionStore.cs`, MCP tools, `DslCompiler.cs:376-397`, tests that call `DomainModelAnalyzer.Analyze` (migrate to `session.Analyze`).

**Not this slice:** Turning `GenerateAllFiles` into a full `session.Emit` contributor civilization; `CompileMode` as `uses http`; deleting the exporter.

`GenerateAllFiles` may remain a thin loop (entity `ToSyntax` + optional DbContext) if it **only reads** session analysis. Do not claim emit-is-the-session until slice 2 is green.

**Becomes true:** Sqlite (and SqlServer) type maps are facts of `session.Analyze`. Evolve/MCP cannot ignore library passes. No second StoragePass in the compiler.

**Still a lie:** `CreateInputs` / `CompileMode.All` still invent HTTP. Runtime ≠ emit for store effects. Comment is still meaning.

**Intended delta (not “identical files”):** `--dbms generic` file set unchanged; `--dbms sqlite` column types **match today’s re-run result**, produced by session analyze. Diagnostics for a fixture match.

**Next red on purpose:** emit still contains `Comment("no-op")`; mixed `if`+`create` still cannot be one AST.

**Stop rg (product):** `DomainModelAnalyzer.Analyze` in `DomainEvolution` and `McpSessionStore`; `new StoragePass(` in `DslCompiler.cs`.

---

### Slice 3 — Honest lowering (emit fail-closed; runtime seam named)

**Invariant:** Unlowerable effects are errors on **emit**. Mixed store effects at **runtime** go through one named seam (`ExecuteStructured`). `Comment` is not shipped meaning.

**First failing test:** `rg 'new Comment\('` empty in `Poly/DomainModeling`. Empty action body goldens are empty blocks or `return`, not comments. An `if { create in … }` action still runs on the instance store.

**Kill / fold**
- `EffectLoweringPass.Composite` / `Conditional` Comment placeholders → throw on emit (top-level lower used by export).
- Exporter `Comment("no-op")` / `Comment("EF materialization")` / `Comment("adapter holder")` → empty block / `return` / throw if that means “we failed to lower.”
- Keep `ExecuteStructured` + `EffectExecutor` as the **named** runtime seam. No new direct-execution effect kind.

**Call sites:** `EffectLoweringPass.cs:406-442`, `DomainToCSharpExporter.cs` Comment sites, `DomainEntityInstance.cs:586-661`. Update goldens that asserted comment text.

**Not this slice:** Delete `ExecuteStructured`. Delete emit-mode `LoweringContext` flags. Host-ABI `CallExternal`. One AST for transition/create/invoke.

**Becomes true:** Export cannot succeed with a Comment standing in for an effect. Runtime mixed composites still work and the seam has one name.

**Still a lie (write this in the PR):** Runtime and emit are **not** the same trees for store effects. `LowerStageTransitions` (emit-mode) still exists. Invariants “shipped ⊆ lowerable” and “one lowering” are **not** locally true for `transition` / `create` / `create in` / `invoke` / `for invoke`.

**Next red on purpose (next suite, not this plan):** store effects as host-ABI calls on both consumers; then delete `ExecuteStructured`.

**Stop rg (product `Poly/DomainModeling`):** `new Comment(`. `ExecuteStructured` **must still exist**.

---

## After slice 3 — stop

Admit nothing else from this file. The next CURRENT, if any, is chosen separately:

| Later work | Why not here |
|------------|----------------|
| Host-ABI `CallExternal` for store effects (then delete `ExecuteStructured` + emit-mode flags) | Interpretation-scoped; only thing that makes runtime == emit |
| Compiler one load path: `ForSource` only; kill `CreateInputs` / Guid `ExtraArtifactLibrary` | Slice 1–2 can leave `--dbms` as seed |
| `uses http` instead of `CompileMode.All` | Product door, not the compile |
| MCP: kill `InferPropertyTypes`; later one `simulate` with explicit kind | Different products (fragment vs named policy vs action) |
| `Packs/` → `Libraries/`, `*Pack*` filenames | Churn |
| Lint-pass merge, catalog dual bags, `ExpressionFormRegistry` collapse, JSON `add` vs `apply_dsl` | Cleanup-inventory leftovers; do not reopen mid-slice |

---

## Not doing

- Grammar rewrite, VM completeness, Synthesis, T2 MCP-from-domain.
- MEF / 12-method plugin / new coordinator.
- Big-bang split of `DomainEntityInstance`.
- Changing `PIPELINE-STATUS.md` CURRENT from this document.
- Claiming MySQL is a `--dbms` seed (`CompilerCatalog` is Core + sqlite + sqlserver only).

---

## Stop conditions (this plan only)

Product trees `Poly/`, `src/`, `Poly.Mcp/` (not `docs/`):

1. `rg failOnUnknown: false` empty. `DomainSession.Open` throws on unknown id.
2. `rg ResolveHost|DomainHostBuilder|DomainParserInputs|FromInputs` empty in product (session holds the tables).
3. `DomainEvolution` / `McpSessionStore` do not call `DomainModelAnalyzer.Analyze`. `DslCompiler` does not `new StoragePass(`.
4. `rg 'new Comment\('` empty under `Poly/DomainModeling`. `ExecuteStructured` still present.

A newcomer can open a session from ids without meeting `DomainHost`. They cannot yet assume emit and runtime share store-effect trees.

---

## Summary

| | |
|---|---|
| **Slices** | 1 one door · 2 analyze sees the session · 3 Comment is not meaning |
| **Deletes** | fail-open session; Host/ParserInputs as public nouns; static analyze on Evolve/MCP; compiler’s second StoragePass; Comment-as-success on emit |
| **Keeps** | Domain, catalog, session, Grammar, DE→AST, VM, `CSharpGenerator`, evolution, `ExecuteStructured` (named), `--dbms` as seed |
| **Still a lie after 3** | One lowering for store effects; `CreateInputs`; `Program.cs` without `uses http`; three MCP simulate shapes; `Packs/` name |
