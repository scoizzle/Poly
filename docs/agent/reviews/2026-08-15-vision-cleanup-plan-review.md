# Vision cleanup plan — 2026-08-15

- **Target**: paths `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md` vs current `HEAD` + dirty tree (plan claims, not a code diff)
- **Mode**: standard
- **Issue counts**: 5 bugs, 8 suggestions, 4 nits
- **Verdict**: **do not admit** until Wave C’s AC is rewritten and Wave B names Evolution + StoragePass type-maps. Architecture lock is sound; several waves cannot meet their own bars on today’s code.
- **Process notes**: the plan absorbed four inventories but dropped remaining cleanup-inventory items (lint/catalog dual, `ExpressionFormRegistry`) without saying so. Counts below were recomputed this pass; do not reuse the plan’s implied “three doors” as complete.

## Summary

The plan’s target (Domain / catalog / session, one lowering, MCP as harness, REST as `uses http`) matches the 2026-08-15 ADR and CORE. The honesty table is mostly right: `DomainHost` still assembles sessions, `DomainModelAnalyzer` is a static cache, `LowerStageTransitions` forks runtime vs emit, MCP has three simulate shapes.

It fails as an executable plan where waves promise “behaviors unchanged” / “byte-identical” / “delete ExecuteStructured” while the code shows those outcomes are mutually exclusive without the host-ABI work they scoped out. Wave B also mis-describes why `GenerateAllFiles` re-runs `StoragePass`, and omits `DomainEvolution` — the product analyze door.

Oracle: no tests were run (review-only). Evidence is current source.

## Issues

### Issue 1 -- Severity: bug
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:102-119` (code: `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:594-611`, `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:406-416`)
- Description: Wave C kill list deletes `ExecuteStructured` and `Comment`-as-success, quarantines `EffectExecutor`, keeps store-effect behavior unchanged, and scopes host-ABI `CallExternal` lowering **out**. Those cannot be true together.
  Today a composite/conditional that contains transition/create/invoke **must** take `ExecuteStructured` because `EffectLoweringPass.Composite`/`Conditional` replace unlowerable children with `Comment` (VM no-op). `ExecuteEffect` documents that path at lines 594–598. Delete `ExecuteStructured` without host-ABI nodes and those actions become throws (if Comment is banned) or silent no-ops (if Comment remains). “Stays green: policy eval + action invoke behaviors unchanged” is then false for any `if` + `create`/`transition`/`invoke` (dogfood already found this class).
- Suggestion: Rewrite Wave C into two parkable slices. **C1:** throw on unlowerable at *top-level* lower for emit; keep `ExecuteStructured` as the named runtime seam for mixed composites (or keep Comment only inside emit goldens you are about to delete). **C2 (admit later):** host-ABI lowering; then delete `ExecuteStructured`. Do not claim invariant 2/3 locally true after C1.
- Status: open

### Issue 2 -- Severity: bug
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:86-90` (code: `src/Poly.DslCompiler/DslCompiler.cs:376-397`, `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs:87`, `Poly/DomainModeling/Analysis/StoragePass.cs:31-37`)
- Description: Wave B says delete `GenerateAllFiles`’s `needsInfraPipeline` re-run because “the session's analyze already ran StoragePass.” Product `UseDomainModelAnalysisPipeline` registers `new StoragePass()` with **no** type maps or conventions. The re-run exists specifically when `analysisInputs.TypeMaps.HasOverrides` or conventions are non-empty (Sqlite `TEXT`/`INTEGER` overrides live on `SqliteDefaults.ApplyTypeMaps`). Deleting the re-run without constructing `StoragePass(typeMaps, conventions)` inside `session.Analyze` changes DbContext column types. “Entities/db/all modes emit byte-identical files” is then false for `--dbms sqlite`.
- Suggestion: Wave B AC: `session.Analyze` must construct `StoragePass` with the session’s `TypeMaps` + `StorageConventions`. Only then delete the compiler re-run. Drop “byte-identical” or restrict it to `--dbms generic`.
- Status: open

### Issue 3 -- Severity: bug
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:39-41` (code: `Poly/DomainModeling/DomainSession.cs:92-93`, `Poly/DomainModeling/Packs/ExtensionCatalog.cs:64-77`)
- Description: Honesty table says catalog opens a host (true) but misses the **fail-open** on the product session door. `ExtensionCatalog.ResolveHost` defaults `failOnUnknown: true`. `DomainSession.Create` calls `ResolveHost(ids, failOnUnknown: false)` and **skips** unknown ids. A domain can list `uses nope` and still open. Wave A fail-closed (“unknown id throws”) would fix this if implemented, but the hole is not in the honesty table, so an implementer can “fold Host into Session” and keep `failOnUnknown: false`.
- Suggestion: Add a honesty-table row. Wave A first failing test: `DomainSession.Open` on unknown id throws. Delete the `failOnUnknown` parameter or default it true with no false overload on the session path.
- Status: open

### Issue 4 -- Severity: bug
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:111` (code: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:216-218`, `287-288`, `321-322`, `461-462`, `531-532`; `LoweringContext.cs:36-40`, `82-88`)
- Description: The plan treats `LowerStageTransitions` as the one consumer flag for stage transitions. In code the same boolean gates **invoke, for-invoke, create, create-in**. It is “C# emit mode,” not “stage transitions.” Sibling consumer forks the plan does not name: `UseThisReference`, `PostTransitionNodes` (subscription notify only on emit), `SourceStageName`, `NavigationNameResolver` / `IsCollectionNavigation` (exists lowering). Deleting only `LowerStageTransitions` after host-ABI work still leaves emit-shaped trees (`this.CreateLoans`, `CurrentStage = Enum`) vs runtime host calls unless those flags die too.
- Suggestion: Rename the debt in the plan: one **emit-mode lowering context**, not one bool. End-state list: every `LoweringContext` flag that changes *which nodes* are produced (not just names) must be gone or must be a host-ABI parameter both consumers share.
- Status: open

### Issue 5 -- Severity: bug
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:129-141` (code: `Poly.Mcp/Tools/OracleTool.cs:370-415`, `Poly.Mcp/Tools/DomainTools.cs:1156`)
- Description: Wave D kill list merges `simulate_policy` and `evaluate_policy` into one “name an entity + context bag” tool, then says “policy + action behaviors unchanged.” `simulate_policy` is **not** a named domain policy: it parses a free expression, builds a synthetic `Entity("Subject")`, infers types from operators (`InferPropertyTypes`), and evaluates with **no session**. `evaluate_policy` requires a session entity + named policy (or instance). Collapsing them either drops the fragment oracle (behavior change) or keeps a no-session expression path (kill list not done). Residual line 141 (“this wave only locks the shape and deletes the type guesser”) contradicts the kill list (fold both tools).
- Suggestion: Split D: **D1** delete/replace `InferPropertyTypes` — fragment simulate must take an entity name + session type map, or die. **D2** one `simulate` tool with an explicit kind (`expression` | `policy` | `action`) and required context. Do not write “behaviors unchanged” across D1/D2.
- Status: open

### Issue 6 -- Severity: suggestion
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:88` (code: `Poly/DomainModeling/Evolution/DomainEvolution.cs:38-39`, `118`; `Poly.Mcp/Sessions/McpSessionStore.cs:59`)
- Description: Wave B files are `DomainModelAnalyzer.cs`, `DomainSession.cs`, `DslCompiler.cs`. Product analyze is `DomainEvolution.Apply` → `DomainModelAnalyzer.Analyze`. MCP refresh is the same. If `session.Analyze` is the door and the static cache remains on evolve/MCP, library `AddPass` still never runs on the authoring path. File list is not the work.
- Suggestion: Wave B kill list: every `DomainModelAnalyzer.Analyze` in product code (`DomainEvolution`, `McpSessionStore`, compiler evolve path). Tests may keep a `session.Analyze` helper. Files: Evolution + MCP session store + all MCP tools that re-analyze.
- Status: open

### Issue 7 -- Severity: suggestion
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:59-61` (code: `src/Poly.DslCompiler/DslCompiler.cs:232-274`, `147-148`)
- Description: Wave A names `ResolveHost` → `Open` but not the **third** assembler: `DslCompiler.CreateInputs` always `Load(Temporal)+Load(Storage)+vendor` and optionally `AddArtifactContributor(MinimalApi…)`, bypassing the catalog. Parse uses `HostForSource` + `CompilerCatalog` (fail-closed). Emit analysis uses `DomainSession.Open(..., failOnUnknown: false)` when `domain.Extensions.Count > 0`. Three load stories. `ExtraArtifactLibrary` synthesizes `artifact:{type}:{guid}` ids so contributors look like libraries without `uses`.
- Suggestion: Honesty-table row: compiler `CreateInputs` / `ExtraArtifactLibrary`. Wave A/B: compiler opens `DomainSession.ForSource(poly, seed, catalog)` only. Host artifacts are a library id (`http`), not a Guid-named fake library.
- Status: open

### Issue 8 -- Severity: suggestion
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:66-67` (code: `src/Poly.DslCompiler/DslCompiler.cs:232-240`, `src/Poly.Packs.MySql/MySqlPack.cs:9-10`)
- Description: Wave A kills `MySqlDefaults` as if MySQL were a compiler seed. `CompilerCatalog` is Core + Sqlite + SqlServer only. `SeedFor` / `ParseDbmsPack` have no `mysql`. MySQL is tests + `Load(new MySqlLibrary())` only. Folding Defaults into Library is fine; implying CLI seed parity is not.
- Suggestion: Wave A: fold Defaults into each `*Library.Register`. Do not add `--dbms mysql` unless you admit a compiler-catalog change. Say MySQL stays extra-library.
- Status: open

### Issue 9 -- Severity: suggestion
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:54-67` vs `docs/plans/domainmodeling-cleanup-inventory-2026-08-15.md` remaining D/E
- Description: Cleanup inventory still lists lint-pass merge, catalog dual (`StageByName` / CrossReference private index), `ExpressionFormRegistry` kitchen sink. This plan never says it **drops** them. An implementer will either reopen those mid-A/B or leave CORE teaching two stories.
- Suggestion: Add “explicitly not this plan (still debt)” bullets for lint merge, catalog dual bags, form-registry collapse, JSON `add`/`remove` vs `apply_dsl`.
- Status: open

### Issue 10 -- Severity: suggestion
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:67`
- Description: Wave A (“delete first, cheapest”) includes `Packs/` → `Libraries/` and `*Pack.cs` / `*PackTests` renames. That is the highest-churn, lowest-semantics item and fights AGENTS smallest loop. `IDomainLibrary` already exists; folder name does not block session-as-compile.
- Suggestion: Park rename after A–C nouns are gone. Stop condition can keep `Packs/` until a dedicated rename slice.
- Status: open

### Issue 11 -- Severity: suggestion
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:117` (code: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:473-476`, `615`, `1325`, `1395`)
- Description: Wave C “export emits identical C#” cannot survive deleting `Comment("no-op")` / `Comment("EF materialization")` / `Comment("adapter holder")`. Those strings are in emitted trees. Fail-closed empty bodies will change goldens (and may be correct).
- Suggestion: Drop identical-C# AC for C. Require golden updates and a test that empty action bodies are real empty blocks or explicit `return`, not comments.
- Status: open

### Issue 12 -- Severity: suggestion
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:145-157` (code: `src/Poly.DslCompiler/DslCompiler.cs:346-361`)
- Description: Wave E says `DomainProgramProjection.ToSyntax` becomes the survivor walk and the exporter folds in. **Entity files already** come from `ToSyntax` + `CSharpGenerator`. The exporter still owns method bodies, subscriptions, Create factories. Calling it a “parallel printer” overstates the dual. The real dual is effect lowering flags + exporter-owned walks for members, not a second entity projection.
- Suggestion: Wave E scope = host library (`http`) + remaining exporter walks that do not go through `ToSyntax`. Do not sell “one printer” as deleting `DomainToCSharpExporter` in the same slice as `uses http` unless the file is already a thin façade.
- Status: open

### Issue 13 -- Severity: nit
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:179`
- Description: Stop condition includes `IDomainPack`. Product `Poly/` and `src/` have **zero** `IDomainPack` types (only historical plan mentions). `Domain.ResolveHost` is also already gone. The rg will stay noisy from this plan file itself if someone greps `docs/`.
- Suggestion: Product-code rg only; drop `IDomainPack` from the kill/stop lists.
- Status: open

### Issue 14 -- Severity: nit
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:155`
- Description: “`--mode all` emits the same four files” is wrong. `GenerateAllFiles` emits one `.cs` per entity, `Poly.Types.cs`, optional `*DbContext.cs`, plus contributor `Program.cs` + `demo.http`.
- Suggestion: “same file set as today for a given fixture,” not “four files.”
- Status: open

### Issue 15 -- Severity: nit
- File: `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs:8`
- Description: Analyzer xmldoc still says “V3 domain models.” Plan residual names archive fog; this is a live product comment the Wave D “strip V3” pass should include (not only MCP tool strings).
- Suggestion: Add `DomainModelAnalyzer` + Evolution comments to the naming strip list.
- Status: open

### Issue 16 -- Severity: nit
- File: `docs/plans/domainmodeling-vision-cleanup-2026-08-15.md:1-8`
- Description: Status “Proposal. Not CURRENT” is correct and should stay until Issues 1–3, 5–6 are edited into the plan. Do not treat this review as admission.
- Suggestion: After plan edits, re-admit check is PIPELINE-STATUS only — human decision.
- Status: open

## What the plan got right (not issues)

- Target shape and “do not rewrite Grammar/VM / do not invent a coordinator” match the lock.
- Honesty that invariant 2/3 are **not** locally true for store effects (until host-ABI) is the right posture — Wave C’s *implementation* AC then betrays it (Issue 1).
- `Comment` sites in `EffectLoweringPass` (3) and `DomainToCSharpExporter` (5) exist as claimed (`rg 'new Comment\('` = 8).
- `DbmsPack`, `--pack`, `CompileMode.All` → `MinimalApiHostArtifactContributor` are real.
- MCP really has three shapes: `simulate_policy` / `evaluate_policy` / `create_instance`+`invoke_action`.
- Parking Synthesis, T2 MCP-from-domain, and `PIPELINE-STATUS` CURRENT is correct.
