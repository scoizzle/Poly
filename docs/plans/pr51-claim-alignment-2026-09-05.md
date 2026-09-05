# PR 51 Claim Alignment — SHA 48a92220

**Date:** 2026-09-05  
**Status:** Proposal — not CURRENT  
**PR:** https://github.com/scoizzle/Poly/pull/51  
**SHA:** `48a922203f1f930a818e40ee5039710541ad0f7b` (head)  
**Merge base:** `master`  
**Source:** PR title/body + `docs/plans/pipeline-transformation-2026-09-04.md` P1–P6

---

## Claim table

| # | Plan/PR claim | Evidence at SHA 48a92220 | Verdict |
|---|---------------|--------------------------|---------|
| **P1** | **One lower.** Create / create-in / unique always lower to Store jobs. C# `Stay.Create` / `CreateNav` are the host bind of those jobs. `LowerStageTransitions` flag removed. | `LowerStageTransitions` has zero references in the tree (grep: 0 matches). `EffectLoweringPass` doc: "Create / create-in / unique lower to Store jobs (`Create` / `CreateIn` / `ProbeCreate` / `EnsureUnique`) on the one operation tree." (`Poly/DomainModeling/Lowering/EffectLoweringPass.cs:17-22`). `DomainToCSharpExporter.StoreBind.cs` defines `BindCreate`, `BindCreateIn`, `ProbeCreate`, `EnsureUnique` as host bind of the Store jobs (`:20-47`). Commit `7bd4f347` ("feat: one Create/CreateIn/EnsureUnique tree and session.Lower") confirms the merge. | **MATCH** |
| **P2** | **Compile once.** Named invoke runs the cached module method `Body`. Bind: stage/enum members → strings, `Notify*Subscribers` → `Notify`, `List<T>.Count` on AST collections, adapter host calls skipped. Subscriptions / transition batches still lower at execute time. | `ExecuteEffectList` (`DomainEntityInstance.cs:650-682`) calls `RuntimeAnalysisCache.GetOrLower()` to get the cached tree, then `TryGetModuleMethod()` (`RuntimeAnalysisCache.cs:77-101`) to locate the named action `MethodDefinitionNode.Body`. `BindModuleMethodBody()` (`:738`) rewrites `ThisReference` for dictionary `This` — not a second lower. Subscriptions/transition batches still call `effectPass.LowerActionBody(effects)` at runtime (`:667`). `Notify*Subscribers` → `Notify` rewrite is in `EffectLoweringPass` (commit `c3bfb722` "fix: bind module method bodies so dictionary This can run them"). `List<T>.Count` on AST collections: commit `c3bfb722` message explicitly lists this. | **MATCH** |
| **P3** | **`session.Lower`.** Cached `DomainProgramProjection.ToSyntax`. `session.Emit` prints that module. | `DomainSession.Lower(Domain, AnalysisResult)` (`DomainSession.cs:137-141`) delegates to `RuntimeAnalysisCache.GetOrLower()` which calls `DomainProgramProjection.ToSyntax()` once and caches the result (`RuntimeAnalysisCache.cs:62-75`). `DomainSession.Emit()` (`:147-178`) calls `Lower()` then `CSharpGenerator` — one call graph. `session.Lower` also named in PR body. | **MATCH** |
| **P4** | **Host artifacts.** `uses http` fail-closed if a `BehaviorAction` is missing from the module. | `DslCompiler.cs:223-240` — `RequireHttpActionsInModule()` throws `InvalidOperationException` if an entity or action named by `uses http` is absent from the lowered module. Commit `7bd4f347` message: "HTTP fail-closed if a named action is missing from the module." | **MATCH** |
| **P5** | **One analysis door.** `DomainSession.Analyze` binds `RuntimeAnalysisCache`. | `DomainSession.Analyze()` (`DomainSession.cs:116-121`) calls `RuntimeAnalysisCache.Bind(domain, this, analysis)`. `RuntimeAnalysisCache.GetOrAnalyze()` (`:46-60`) reuses the bound session (`holder.Session.AnalyzeWithoutBind(domain)`), vendor maps included. Unbound fallback opens core-catalog only when nothing has bound yet (`:122-126`). | **MATCH** |
| **P6** | **Clocks in the tree.** Clocks lower to BCL members the VM executes. `PreprocessRuntimeKeyword` removed. | `PreprocessRuntimeKeyword` has zero references (grep: 0 matches). `EffectLoweringPass.LowerDefaultExpression()` (`:952+`) maps `now`/`utcnow` → `DateTime.UtcNow`, `today` → `DateTime.Today` / `DateOnly.FromDateTime(DateTime.Today)`, `guid` → `Guid.NewGuid()` — BCL members the VM executes, not host literals. Commit `8f12c31c` ("feat: keep now/today/guid in the operation tree") confirms. | **MATCH** |
| **Create defaults on probe** | Store `Create` / `ProbeCreate` fill `default(...)` before unique/required validation. | `DomainInstanceStore.ProbeCreate()` (`:130-153`) calls `FillCreateDefaults(target, scalars, creator.Domain)` (`:151`) before `ValidateCreateConstraints()`. `CreateCore()` (`:175`) likewise fills defaults before constraint check. Commit `20c92f9b` ("fix: probe and create see defaulted unique/required values") confirms. | **MATCH** |

---

## Gaps

**None.** All seven claims (P1–P6 + create-defaults-on-probe) match shipped evidence at SHA 48a92220.

---

## Additional observations (not claims)

- **CI:** PR body states 2669 tests passed at HEAD. Build succeeds at this SHA (`dotnet build` — 0 warnings, 0 errors).
- **`EffectExecutor` deleted:** Commit `c780b377` ("refactor: delete EffectExecutor (all arms were throws; no callers remaining)"). Only references in comments and test names (`:424`, `StageTransitionHostAbiTests.cs:82`, `ActionEntityReturnTests.cs:1008`).
- **PIPELINE-STATUS.md:** Current is `(none)` — PR correctly does not claim CURRENT.
- **Pipeline transformation plan:** P1–P6 status section updated to reflect execution. Plan remains "Not CURRENT, not a suite."

---

## Recommendation

All P1–P6 claims and the create-defaults-on-probe claim are factually aligned with the code at SHA 48a92220. No overclaims, no gaps, no partial matches. The PR body accurately describes what shipped.
