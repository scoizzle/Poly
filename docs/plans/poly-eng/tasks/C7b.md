# TASK C7b - Three action-by-name lookups use the index
Status: in review. Branch slice/c7b-plan-v2. Lane B. Review 2. Implement mill: Grok (review on OpenCode).

## Scope
C7a (merged `#150` `3b53f0fa`) left 15 `GetOrAnalyze` call sites (16 `git grep` hits including the definition at `RuntimeAnalysisCache.cs:82`). The three action-by-name sites still resolve through analysis ARM + `TryResolveAction` (stage-first, SA empty-copy at run time): `InvokeActionInternal` `DomainEntityInstance.cs:609-614`, `ResolveActionForNamedInvoke` `InvokeNamed.cs:185-187`, and the unresolved arm `InvokeNamed.cs:44-47` (feeds `ReportUnresolvedAction`). Compile already bakes that fallthrough into one method per name (`AddStageDispatchedActionMethod` `DomainToCSharpExporter.Actions.cs:126-138`). This slice is the first `CatalogIndex` consumer: those three sites look up `index.Method(Entity.Name, actionName)` after Lower, drop their `GetOrAnalyze`, and keep `ResolveStandaloneAction` for the ontology `Action` (params/result). `GetOrLower(Domain)` (`RuntimeAnalysisCache.cs:101-104`) does not fill `Session.ArtifactCatalog` (only `DomainSession.Lower` `:181-187` does), so add `Index(Domain)` next to that accessor that Lowers then `new CatalogIndex(session.ArtifactCatalog)`.

## Files
- `Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs` — add `internal static CatalogIndex Index(Domain domain)`: `EnsureAnalysis`, `Session(domain).Lower`, `new CatalogIndex(session.ArtifactCatalog)`. `CatalogIndex.Method` / `Tree` unchanged (no stage argument).
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` — `InvokeActionInternal` `:605-622`: drop `GetOrAnalyze` / `GetActionResolution` / `TryResolveAction`; Domain-bound uses `Index(Domain).Method` + `ResolveStandaloneAction`; `ReportUnresolvedAction` gets `analysis: null` (Entity.Stages walk `:384-387` already reports StageRequired). `:99` Create stays (C7c/C7d).
- `Poly/DomainModeling/Runtime/DomainEntityInstance.InvokeNamed.cs` — `ResolveActionForNamedInvoke` `:183-190` same pair; unresolved arm `:44-47` stops calling `GetOrAnalyze`. `:149` When-handler stays (C7d).
- `Poly.Tests/DomainModeling/Compile/Item3LowerPopulateTests.cs` — one row: `Index(domain).Method` after `GetOrLower(domain)` only (no prior `session.Lower`) is the same `MethodDefinitionNode` as `TryGetModuleMethod`.
- Named and left alone: `CatalogIndex.cs` surface, `TryResolveAction` / `DomainSemanticLookupFailClosedTests`, `TryGetModuleMethod` (ExecuteEffectList `:812`), HostAbi / Runtime.cs / Store / BehaviorMetadata / OracleTool, MCP `RuntimeTool.cs:50`.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Domain-bound `InvokeAction("AssessByType")` | `GetOrAnalyze` + ARM + `TryResolveAction` (`:609-614`) | `Index(Domain).Method("Patron", "AssessByType")` then `ResolveStandaloneAction`; execute still binds the module body | `DomainEntityInstanceTests` action rows; `ParityTests` |
| Stage-scoped action from the wrong stage (`Ship` while Pending) | ARM miss → `ReportUnresolvedAction` with analysis (`:370-391`) → "only available in stage 'Paid'" | same message via Entity.Stages walk (`:384-387`); method exists on the tree and is not invoked | `InvokeAction_StageScopedFromWrongStage_ReportsRequiredStage` (`DomainEntityInstanceTests` `:3798-3814`); `ActionEntityReturnTests` `:900` |
| SA empty stage-copy (no effects/policies) | `TryResolveAction` `:164-170` returns the entity action | `ResolveStandaloneAction` `:58-62` already the same predicate; compiled method already inlines the entity body (`Actions.cs:126-131`) | `DomainSemanticLookupFailClosedTests.TryResolveAction_EmptyStageCopy_*` unchanged; action invoke rows |
| `InvokeNamed("AssessByType", …)` | `GetOrAnalyze` + `TryResolveAction` (`InvokeNamed.cs:185-187`) | `Index(Domain).Method` + `ResolveStandaloneAction` | `WhenAnySimulatePrintAgreeTests` / `DomainEntityInstanceTests` named invoke |
| Unknown name | `GetOrAnalyze` for `ReportUnresolvedAction` (`InvokeNamed.cs:44-47`) | `analysis: null`; Missing, not StageRequired | `DomainEntityInstanceTests` missing-action rows |
| `Index` before any `session.Lower` | `ArtifactCatalog` empty after `GetOrLower(Domain)` (`:101-104`) | `Index` Lowers; `Method` matches `TryGetModuleMethod` | new `Item3LowerPopulateTests` row |
| Analysis with Errors (G1) | `GetOrLower` throws the first error | `Index` → `Lower` still throws that error | `CompileRefusesErrorsTests`; simulator-refuses rows |

## Done when
- The three sites above do not call `GetOrAnalyze`. `git grep GetOrAnalyze -- Poly Poly.Mcp` is 13 hits (was 16), 12 call sites excluding the definition at `RuntimeAnalysisCache.cs:82`. `InvokeNamed.cs` reads 1 (`:149`); `DomainEntityInstance.cs` reads 1 (`:99`).
- `git grep -n CatalogIndex -- Poly` is `Compile/CatalogIndex.cs` plus `RuntimeAnalysisCache.Index` (and its three callers if they name the type). No stage parameter on `Method`.
- `TryResolveAction` and its fail-closed tests are untouched. Suite green; StageRequired wording unchanged; G1 refuse tests still pass.

## SHIP if / NOT SHIP if
SHIP if the product `GetOrAnalyze` count drops by exactly 3, the three sites resolve through `Index(Domain).Method`, `CatalogIndex.Method` still takes `(treeName, methodName)` only, and the suite is green with the StageRequired rows passing unchanged.

NOT SHIP if any of the three sites keeps `GetOrAnalyze` or adds a second cache read, if `Method` grows a stage argument, if a behavior test was edited to fit, or if HostAbi / Store / Create `:99` / When `:149` moved (those are C7c/C7d).

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Item3LowerPopulateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CatalogIndexTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainSemanticLookupFailClosedTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ActionEntityReturnTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CompileRefusesErrorsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
One `Index` factory next to `GetOrLower(Domain)` and three resolve-site replacements; Scot can open `Index` and the two resolve methods cold.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | grok/grok-4.6 | 3b53f0fa | planned | - |
| 2026-10-10 | implementer | grok/grok-4.6 | 5b795c31 | pushed | tests 3517/3517; sweep: Index.Method not a gate; remaining GetOrAnalyze listed |
| 2026-10-10 | reviewer | opencode/opencode-go/deepseek-v4.1-flash | e2387c1c | NOT SHIP | https://github.com/scoizzle/Poly/pull/153#issuecomment-6100109115 (Grug NOT SHIP at e2387c1c, 2 open findings, mode full) |
| 2026-10-10 | implementer | grok/grok-4.6 | 0b571dc4 | fixes pushed | R1, R2; disputed: none; tests 3517/3517 |
