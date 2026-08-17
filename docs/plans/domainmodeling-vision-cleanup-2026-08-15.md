# DomainModeling — vision cleanup (three nouns, one compile, one lowering)

**Date:** 2026-08-15 (edited for review follow-ups F1–F13)
**Status:** **Superseded** by [`domainmodeling-vision-cleanup-2026-08-16.md`](domainmodeling-vision-cleanup-2026-08-16.md). Do not execute. Kept as the five-wave draft the 2026-08-15 review attacked.
**Lock:** [`docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](../decisions/2026-08-15-domain-library-extensions-mcp-harness.md) + [`2026-08-14-domain-libraries.md`](../decisions/2026-08-14-domain-libraries.md) + `docs/CORE.md` + AGENTS platform facts.
**Absorbs (not competing):** `domainmodeling-cleanup-inventory-2026-08-15.md`, `domainmodeling-session-is-the-compile-2026-08-15.md`, `domainmodeling-extension-architecture-2026-08-15.md`, `domainmodeling-metadata-artifact-catalog-2026-08-15.md`, `complexity-semantic-map.md`, `interpretation/domain-execution-model.md`.
**Review:** [`docs/agent/reviews/2026-08-15-vision-cleanup-plan-followups.md`](../agent/reviews/2026-08-15-vision-cleanup-plan-followups.md).

**This plan changes the story, not the spine.** The product path stays `.poly → Domain → session load → analyze → lower → export/VM`. It deletes the leftover composition nouns and the residual dual paths so the tree matches the lock. **No Grammar rewrite. No VM completeness. No new IR, coordinator, MEF, or 12-method plugin.**

---

## 0. Target shape (the only architecture)

```text
Domain          = facts (types, navs, contracts, uses ids). Not a process. No Main.
ExtensionCatalog = process: id → IDomainLibrary
DomainSession   = the compile: Language, folds, meaning, passes, artifacts
Lowering        = one complete, legal Syntax AST per shipped action/policy/create/subscription
Interpretation  = backbone that runs/emits that AST (VM + CSharpGenerator)
Extensions      = opt-in (uses). Meaning / persistence / product doors (REST). Hosts bind already-lowered ops.
Poly.MCP        = interactive harness: author, inspect, simulate named ops with caller-supplied context.
                  Holds a DomainSession; is not that session; is not the customer API.
```

## Invariants (preserve; cleanup must make these *locally* true)

1. **Shipped ⊆ lowerable.** New spell waits in `docs/plans/` and stays out of parser + guide.
2. **Runtime and emit consume the same lowered trees.** No consumer lowering flags (the emit-mode `LoweringContext`, not just `LowerStageTransitions`) in the end state.
3. **No `Comment` / null-from-lowering / `EffectExecutor` / `ExecuteStructured` as shipped meaning.** Residual dual-path is debt to delete or quarantine, not to document as a feature.
4. **Core seed does not emit `Program.cs`.** CLI flags seed `uses` ids only.
5. **MCP simulate = name the operation + supply context + Interpreter on that AST.** Collapse `simulate_policy` / `evaluate_policy` / `create+invoke` toward one harness shape.
6. **One library type** (`Id` + `Register`). `Packs/` and `*Pack` names are leftovers.
7. **Analyze and emit run on the session.** No static `DomainModelAnalyzer` cache ignoring session passes; no `GenerateAllFiles` civilization beside contributors.

---

## 1. Honesty — where the lock and the code disagree today

| Lock says | Code still does | Where |
|-----------|-----------------|-------|
| Domain is facts; catalog opens a session | `ExtensionCatalog.ResolveHost(ids) → DomainHost`; `DomainSession.Create` assembles via `DomainHost`/`DomainHostBuilder`/`DomainParserInputs`/`DomainAnalysisInputs` | `Poly/DomainModeling/DomainHost.cs`, `Packs/ExtensionCatalog.cs`, `DomainSession.cs` |
| Unknown `uses` id fails closed | `DomainSession.Create` calls `ResolveHost(ids, failOnUnknown: false)` and **skips** unknown ids — a domain can list `uses nope` and still open | `DomainSession.cs:92-93`, `ExtensionCatalog.cs:64-77` |
| One load story (catalog opens a session) | A third assembler: `DslCompiler.CreateInputs` always `Load(Temporal)+Load(Storage)+vendor`, optionally `AddArtifactContributor(MinimalApi…)`; `ExtraArtifactLibrary` synthesizes `artifact:{type}:{guid}` ids so contributors bypass `uses` | `DslCompiler.cs:232-280`, `147-148` |
| Analyze runs on the session | `DomainModelAnalyzer` is a static cached `Analyzer` with a fixed pipeline; `DomainSession.Analysis` holds only type maps + storage conventions, not passes; the product analyze door is `DomainEvolution.Apply` → `DomainModelAnalyzer.Analyze` | `Analysis/DomainModelAnalyzer.cs`, `Evolution/DomainEvolution.cs:38-39,118`, `Poly.Mcp/Sessions/McpSessionStore.cs:59` |
| Emit runs on the session | `DslCompiler.GenerateAllFiles` inline-emits entities + DbContext; `CompileMode` is an enum beside the artifact list | `src/Poly.DslCompiler/DslCompiler.cs` |
| CLI flags seed `uses` ids only | `DbmsPack` enum + `--pack` alias + `SeedFor`/`ResolveDbms`/`DbmsPacks` | `src/Poly.DslCompiler/DslCompiler.cs`, `Program.cs` |
| One lowering; runtime == emit | An **emit-mode lowering context** forks the same effects: `LowerStageTransitions` gates `transition to`, `invoke`, `for … invoke`, `create`, `create in`; siblings `UseThisReference`, `PostTransitionNodes`, `SourceStageName`, `NavigationNameResolver`/`IsCollectionNavigation` also change which nodes are produced | `Lowering/LoweringContext.cs:36-40,82-88`, `EffectLoweringPass.cs:216-218,287-288,321-322,461-462,531-532` |
| No `Comment` / second interpreter as meaning | `EffectLoweringPass` emits `Comment` for unlowerable sub-effects; exporter emits `Comment("no-op")`/`Comment("EF materialization")`/`Comment("adapter holder")` (8 `new Comment(` sites total); `DomainEntityInstance.ExecuteStructured` + `EffectExecutor` are the direct-execution path; a composite/conditional with transition/create/invoke **must** take `ExecuteStructured` today | `EffectLoweringPass.cs:406-416`, `DomainToCSharpExporter.cs:473-476,615,1325,1395`, `DomainEntityInstance.cs:594-611` |
| One simulate-with-context | `simulate_policy` (no session; builds synthetic `Entity("Subject")` + ad-hoc `InferPropertyTypes`), `evaluate_policy` (session entity + named policy/instance), `create_instance`+`invoke_action` (store) are three shapes | `Poly.Mcp/Tools/OracleTool.cs:370-415`, `DomainTools.cs:1156`, `RuntimeTool.cs` |
| Core seed has no `Program.cs` | `CompileMode.All` triggers `MinimalApiHostArtifactContributor` | `src/Poly.DslCompiler/` |

The plan below does not write CORE as if these are already gone. Waves A–E land them one at a time; C is split so the tree never claims invariant 2/3 are locally true before the host-ABI work exists.

---

## Wave A — Composition nouns (delete first, cheapest clarity)

**Goal:** teach `Domain / ExtensionCatalog / DomainSession`. One type holds `Language + folds + meaning + passes + artifacts`; the catalog returns sessions, not hosts. Fail-closed on unknown ids.

**Kill list**
- `DomainHost`, `DomainHostBuilder`, `DomainParserInputs`, `DomainAnalysisInputs` — fold fields onto `DomainSession` (`Poly/DomainModeling/DomainHost.cs`).
- `ExtensionCatalog.ResolveHost` → replace with `Open(ids) → DomainSession` (or keep `Resolve(id)` and build the session inside `DomainSession.Open`).
- `DomainCompilation.HostForSource` → returns a `DomainSession`.
- `DomainSession.FromInputs` (side door rebuilding Temporal meaning) → delete.
- `IDomainLibrary.Register(DomainHostBuilder)` → `Register(SessionBuilder)` (rename the builder noun; the one-type shape is already correct).
- `DbmsPack` enum + `SeedFor` / `DbmsPacks` / `ResolveDbms` / `ParseDbmsPack` + the `--pack` CLI alias.
- `SqlServerDefaults` / `SqliteDefaults` / `MySqlDefaults` static classes → fold into each `*Library.Register` (type maps + conventions). **MySQL is not a compiler `--dbms` seed** — `CompilerCatalog` is Core + Sqlite + SqlServer only; MySQL stays an extra library loaded by tests/`Load`.

**Files:** `Poly/DomainModeling/DomainHost.cs`, `DomainSession.cs`, `Packs/ExtensionCatalog.cs`, `Packs/DomainCompilation.cs`, `Packs/IDomainLibrary.cs`, `src/Poly.DslCompiler/DslCompiler.cs` + `Program.cs`, `src/Poly.Packs.*/*Defaults.cs`.

**First failing test (unknown id):** `DomainSession.Open` on a domain listing `uses nope` throws. Delete the `failOnUnknown` parameter on the session path (or default true with no `false` overload). Do not carry `failOnUnknown: false` through a "fold Host into Session" refactor.

**Stays green:** full suite; `DslCompiler --dbms sqlite` still seeds id `sqlite` and emits the same file set; MCP parse/print unchanged.

**Fail-closed:** unknown/duplicate library id throws; `rg 'ResolveHost|DomainHostBuilder|DomainParserInputs|DomainAnalysisInputs|DbmsPack'` empty in product code.

**Residual (parked, not this wave):** `Packs/` → `Libraries/` folder rename and `*Pack.cs` → `*Library.cs` file renames — highest churn, lowest semantics; `IDomainLibrary` already exists and the folder name does not block session-as-compile. Do after A–C nouns are gone, in a dedicated rename slice. `Domain` itself (facts), `DomainSuite` / `InternalDomainProducer` (contract fill — not session load).

---

## Wave B — Session is the only compile

**Goal:** `session.Analyze(domain)` and `session.Emit(domain, analysis)` are the doors — including the **authoring** path. Delete the static pipeline and the inline emit.

**Kill list**
- Static `DomainModelAnalyzer` cached analyzer → becomes `session.Analyze(domain)`. `UseDomainModelAnalysisPipeline` becomes the **core seed** pass list registered at `Open`.
- **Every product `DomainModelAnalyzer.Analyze` call site**, not just the type: `DomainEvolution.Apply` (the authoring gate) and `McpSessionStore` refresh, plus the compiler evolve path. Tests may keep a `session.Analyze` helper.
- `DomainSession.Analysis` (`DomainAnalysisInputs`) → replaced by an ordered `Passes` list (libraries append; duplicate name fails closed; `INodeAnalyzer.Dependencies` already orders).
- `DslCompiler.GenerateAllFiles` inline entity/DbContext emit → core **contributors** (entity module + DbContext) on `session.Artifacts`. `CompileMode` becomes "which seed contributors are loaded", not a parallel emit path.
- `GenerateAllFiles`'s `needsInfraPipeline` StoragePass re-run — **only after** `session.Analyze` constructs `StoragePass` with the session's `TypeMaps` + `StorageConventions` (today the product pipeline registers `new StoragePass()` with neither, which is exactly why the re-run exists for `--dbms sqlite`'s `TEXT`/`INTEGER` overrides). Deleting the re-run before that changes DbContext column types.

**Files:** `Analysis/DomainModelAnalyzer.cs`, `Evolution/DomainEvolution.cs`, `Poly.Mcp/Sessions/McpSessionStore.cs`, any MCP tool that re-analyzes, `DomainSession.cs`, `src/Poly.DslCompiler/DslCompiler.cs`.

**Stays green:** per-fixture file-set parity (entity files + `Poly.Types.cs` + optional `*DbContext.cs` + contributor files); analysis parity (same diagnostics). **Do not** claim "byte-identical" — restrict it to `--dbms generic` if claimed at all.

**Fail-closed:** structural failure → no artifact contributes; missing catalog → throw; unknown library pass → throw (not silently skipped).

**Residual:** the exporter walk (`DomainProgramProjection` vs `DomainToCSharpExporter`) — Wave E.

---

## Wave C — Meaning clamp (split: C1 now, C2 later)

### C1 — fail-closed unlowerable on emit (do now)

**Goal:** an effect that cannot lower is an error, not a `Comment` no-op. Keep the runtime seam honestly named; do **not** claim one lowering yet.

- `Comment`-as-success: `EffectLoweringPass.Composite`/`Conditional` emit `Comment` for unlowerable sub-effects; exporter emits `Comment("no-op")`/`Comment("EF materialization")`/`Comment("adapter holder")`. Replace with fail-closed (throw on unlowerable) at top-level lower for emit; empty action bodies become real empty blocks or explicit `return`, never comments.
- Keep `ExecuteStructured` as the **named runtime seam** for mixed composites (a composite/conditional containing transition/create/invoke must still run its sub-effects today — `DomainEntityInstance.cs:594-611`). It is quarantine, not a feature; no new direct-execution effect kind enters through it.
- `PreprocessQuantifiers` / `PreprocessRuntimeKeyword` / `PreprocessEffectExpressions` become the **one** store/clock pre-lower — the ADR §5 host ABI (create, link, notify, outbound, time/id) — not a second evaluator.

**Stays green:** policy eval + action invoke behavior is preserved **only where C1 does not change emit semantics**; C1 **does** change emit (fail-closed + real empty bodies), so require **updated goldens** and a test that empty action bodies are real empty blocks or `return`, not comments. Do not claim "behaviors unchanged" or "identical C#".

**Fail-closed:** unlowerable effect throws (no silent drop); missing store/domain for store-aware forms throws.

**Not claimed after C1:** invariant 2/3 are **not** locally true. The dual path still exists; it is now explicit and shrinkable.

### C2 — host-ABI lowering (admit later, then delete the seam)

**Goal:** the five store effects (`transition to`, `create`, `create in`, `invoke`, `for … invoke`) lower to **one complete AST for both runtime and emit** — generic host-ABI external-call nodes (create, link, notify, outbound, time/id), not null (runtime) vs inline C# (emit).

- Delete `ExecuteStructured` + `ContainsDirectExecutionEffect` + `EffectExecutor` only after store effects lower to host-ABI calls.
- Delete the **emit-mode lowering context**, not just `LowerStageTransitions`: every `LoweringContext` flag that changes *which nodes* are produced (`LowerStageTransitions`, `UseThisReference`, `PostTransitionNodes`, `SourceStageName`, `NavigationNameResolver`/`IsCollectionNavigation`) must die or become a host-ABI parameter both consumers share.

**Blocked on:** host-ABI `CallExternal` lowering for store effects — an Interpretation/VM-scoped change, **scoped here, not implemented** (see §Not doing). This is the single change that makes invariant 2/3 fully true.

**Explicit shrink statement:** the five store effects are shipped and execute today only because the runtime has `EffectExecutor` and the exporter has `LowerStageTransitions: true`. Neither is "one lowering". If a construct is found that cannot lower even to a host-ABI call, it is **shrunk out of the shipped DSL** (waits in `docs/plans/`).

**Files:** `Lowering/EffectLoweringPass.cs`, `Lowering/LoweringContext.cs`, `Lowering/DomainToCSharpExporter.cs`, `Runtime/DomainEntityInstance.cs`.

**Residual:** `DomainEntityInstance` god-object split (not this plan).

---

## Wave D — Harness clamp (MCP)

### D1 — delete the no-session fragment oracle (do now)

- Kill `OracleTool.SimulatePolicy` (no session) + its ad-hoc `InferPropertyTypes` / `InferClrType` / `CollectPropertyNames` type-guessing. A fragment simulate must name an entity + take the session type map, or die. No type guessing from operators.

### D2 — one `simulate` tool with an explicit kind (admit later)

- One `simulate` tool: explicit `kind` ∈ `expression` | `policy` | `action`, plus required context (entity, bag, links, clock). Folds `evaluate_policy` and the store-backed `create_instance`+`invoke_action` surface into the same contract shape.
- The `create_instance` + `invoke_action` runtime surface stays (it is the store + subscription fan-out); its description is renamed to "run this operation against the session store", not a second evaluator.
- Tool descriptions: strip "Phase 1a/1b", "V3", "Q3′", "no session required"; no inferred `Main`. Include live product comments: `DomainModelAnalyzer.cs:8` ("V3 domain models") + `DomainEvolution` comments.

**Files:** `Poly.Mcp/Tools/OracleTool.cs`, `Poly.Mcp/Tools/DomainTools.cs`, `Poly.Mcp/Tools/RuntimeTool.cs`.

**Stays green:** policy + action behaviors are preserved **within D1** (fragment oracle loses its no-session mode — that is the intended behavior change) and **within D2**; do not claim "behaviors unchanged across the merge".

**Fail-closed:** missing context → reject; unlowered operation → reject (not `Comment` success); unknown `kind` → reject.

---

## Wave E — Host extensions (uses http)

**Goal:** `Program.cs` / `demo.http` / Minimal API become `uses http`, not `CompileMode.All`. Compiler opens `DomainSession.ForSource(poly, seed, catalog)` only — no `CreateInputs`/`ExtraArtifactLibrary` Guid-id path (F7).

**Kill list**
- `MinimalApiHostArtifactContributor` + `MinimalApiGenerator` triggered by mode → a `HttpLibrary` (id `http`) registered by `uses http`. `--mode all` → seeds `uses http` (+ vendor id). Core seed emits only the entity module; DbContext is persistence (`storage`/vendor id).
- `DslCompiler.CreateInputs` + `ExtraArtifactLibrary` (Guid-named fake libraries) → `DomainSession.ForSource`. Host artifacts are a library id, not a synthesized id.

**Scope — not "delete the parallel printer":** entity files **already** emit via `DomainProgramProjection.ToSyntax` + `CSharpGenerator` (`DslCompiler.cs:346-361`). The remaining exporter-owned walks are method bodies, subscriptions, `Create` factories. Wave E folds those walks into the `ToSyntax` path (or the exporter is already a thin façade). Do not sell this slice as deleting `DomainToCSharpExporter` wholesale.

**Files:** `src/Poly.DslCompiler/DslCompiler.cs`, `MinimalApiGenerator.cs`, `Poly/DomainModeling/Lowering/DomainProgramProjection.cs`, `DomainToCSharpExporter.cs`.

**Stays green:** `--mode all` emits the **same file set as today for a given fixture** (not "four files" — it's one `.cs` per entity, `Poly.Types.cs`, optional `*DbContext.cs`, plus contributor `Program.cs` + `demo.http`); `export_domain_to_csharp` unchanged.

**Fail-closed:** host extension binds only already-lowered operations; an operation that did not lower fails closed in the extension, never completes missing lowering in strings.

---

## Explicitly not this plan (still debt; do not reopen mid-wave)

- **Lint-pass merge** (AuthoringSuggestion/RuleCoverage/CrossReference hint collapse) and **catalog dual bags** (`EntityStructureMetadata.StageByName`, `CrossReferencePass` private rel index) — from cleanup-inventory C.
- **`ExpressionFormRegistry` kitchen-sink collapse** (forms + folds + meaning + print maps) — cleanup-inventory B.
- **JSON `add`/`remove` vs `apply_dsl`** two-mutation-language consolidation (the `dsl-delta-fragments` direction) — admitted separately.

---

## Not doing (scoped out, re-admit later against CORE)

- **Grammar rewrite** — LeftAssoc live-fold (D4), token/exception revision (D17). Parked.
- **VM completeness / host-ABI `CallExternal` lowering for store effects** — the C2 unblock. Scoped, not implemented here.
- **Synthesis / macros.**
- **T2 self-hosting MCP from a domain.**
- **MEF, discovery host, 12-method plugin, a new IR, a new coordinator type.**
- **Big-bang split** of `DomainEntityInstance` or the exporter in a single wave (smaller tested loops; park between waves).
- **Do not change `PIPELINE-STATUS.md` CURRENT.**

---

## Order and stop conditions

A → B → C1 → D1 → C2 → D2 → E. Each slice is parkable with its own green bar and fail-closed checks. Stop when:

- `rg 'ResolveHost|DomainHostBuilder|DomainParserInputs|DomainAnalysisInputs|DbmsPack|GenerateAllFiles'` is empty in **product code** (`Poly/`, `src/`, `Poly.Mcp/`), not docs (drop `IDomainPack` — already dead).
- `DomainSession.Open` throws on unknown `uses` id; no `failOnUnknown: false` path.
- `session.Analyze` is the only analyze door in product code (no `DomainModelAnalyzer.Analyze` in `DomainEvolution`/`McpSessionStore`); `StoragePass` receives session type maps + conventions.
- `rg 'new Comment|ExecuteStructured'` is empty in `Poly/DomainModeling`; `EffectExecutor` appears in exactly one named seam (until C2 deletes it).
- `simulate` is one tool with an explicit `kind`; no tool description says "Phase 1a", "V3", "Q3′", or "no session required".
- `Program.cs` is emitted only when `uses http` (or its seed) is present; the compiler has one load path (`DomainSession.ForSource`).
- A newcomer implements `uses Foo` by reading `Domain`, `DomainSession`, the Catalog pass, `IDomainLibrary`, and one Foo file.

---

## Summary

| | |
|---|---|
| **Waves** | A composition nouns · B session-is-the-compile · C1 fail-closed unlowerable (keep runtime seam) → C2 host-ABI lowering (then delete the seam) · D1 kill fragment oracle → D2 one `simulate` tool · E `uses http` |
| **Biggest deletions** | `DomainHost`/`DomainHostBuilder`/`DomainParserInputs`/`DomainAnalysisInputs`; static `DomainModelAnalyzer` (all call sites); `GenerateAllFiles`; `DbmsPack`+`CompileMode` as enums; `Comment`-as-success; `simulate_policy` ad-hoc type guesser; `MinimalApiHostArtifactContributor`-by-mode; `CreateInputs`/`ExtraArtifactLibrary` third load path. |
| **What stays** | Domain (facts) · `ExtensionCatalog` · `DomainSession` (grown into the compile) · Grammar engine · DE→AST lowering · VM + `CSharpGenerator` · evolution/`DomainChange` · fail-closed analysis · `ImportedContract` · Temporal/storage/sqlite as `IDomainLibrary` seeds. |
| **Still debt after the last wave** | Store effects (`transition/create/create-in/invoke/for`) still run through the quarantined `EffectExecutor`/`ExecuteStructured` seam until C2's host-ABI `CallExternal` lowering lands → invariant 2/3 are **not** fully local for store effects until C2. `DomainEntityInstance` god-object split. Lint merge + catalog dual bags + `ExpressionFormRegistry` collapse + JSON `add`/`remove` consolidation (F9, not this plan). Archive/plan fog + `V3`/`Phase 1a` naming. LINQ oracle + Text stdlib + VM perf positioning (out of this lock's scope). |
