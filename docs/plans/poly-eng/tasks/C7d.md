# TASK C7d - One-offs and BehaviorMetadata
Status: in review. Branch slice/c7d-plan. Lane A. Review 1. Implement mill: Grok (review on OpenCode).

## Scope
Card `**C7d**` (`docs/domain-modeling/pipeline-convergence-plan.md:573-580`; Depends on C7c, merged `78231090`) closes the C7-0..C7d lookup group: "Stage enum names, subscription fan-out, storage target, `BehaviorMetadata`" and "`git grep GetOrAnalyze -- Poly/DomainModeling/Runtime` is empty." C7b added `RuntimeAnalysisCache.Index(Domain)` (`Analysis/RuntimeAnalysisCache.cs:114-129`); C7a added the 1-arg `GetOrLower(Domain)` (`:104-107`). C7c left nine `GetOrAnalyze` sites in `Runtime/` plus one in `Analysis/BehaviorMetadata.cs:47` and one in `Poly.Mcp/Tools/OracleTool.cs:420`. Two of the nine are pure pairs (`HostAbi.cs:313-315`, `:363-365`) where `analysis` is used only to feed the 3-arg `GetOrLower`, so they collapse to `GetOrLower(Domain)` exactly as C7a did. The other seven read analysis metadata (`SubscriptionDispatchPlanMetadata` `:327`; `DomainCatalogMetadata` `:142`; `StorageMappingMetadata` `Store.cs:104`; `RelationshipContractMetadata` `Store.cs:370`; `ResolvedTypeReferenceMetadata` `BehaviorMetadata.cs:76`) and need `AnalysisResult`; the cache already holds it via the private `EnsureAnalysis` (`:88-102`). This slice adds one `internal static AnalysisResult Analysis(Domain)` accessor over `EnsureAnalysis` and routes every product site through it. `GetOrAnalyze` keeps its public signature and body for the 12 test files (C8d deletes it and moves those tests); `Index` and `CatalogIndex` are untouched.

## Files
- `Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs` — add `internal static AnalysisResult Analysis(Domain domain) => EnsureAnalysis(domain);` next to `GetOrLower(Domain)` `:104` / `Index` `:114`; `GetOrAnalyze` `:85-86` unchanged.
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` — `:141` `RuntimeAnalysisCache.GetOrAnalyze(Domain)` → `RuntimeAnalysisCache.Analysis(Domain)`; `:313-315` pair → `RuntimeAnalysisCache.GetOrLower(Domain);`; `:363-365` pair → `RuntimeAnalysisCache.GetOrLower(Domain);`.
- `Poly/DomainModeling/Runtime/DomainEntityInstance.InvokeNamed.cs` — `:146` → `RuntimeAnalysisCache.Analysis(Domain)`.
- `Poly/DomainModeling/Runtime/DomainEntityInstance.Runtime.cs` — `:304` → `RuntimeAnalysisCache.Analysis(domain)`.
- `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` — `:103`, `:470`, `:492` → `Analysis(domain)`; `:369` → `Analysis(source.Domain)`.
- `Poly/DomainModeling/Analysis/BehaviorMetadata.cs` — `:47` `BuildBehavior` → `RuntimeAnalysisCache.Analysis(domain)`.
- `Poly.Mcp/Tools/OracleTool.cs` — `:420` → `RuntimeAnalysisCache.Analysis(domain)`.
- `Poly.Tests/DomainModeling/Compile/Item3LowerPopulateTests.cs` — one row next to `Index_AfterGetOrLowerOnly_MethodMatchesTryGetModuleMethod` `:258`: `Analysis(domain)` is reference-equal to `GetOrAnalyze(domain)` after one call.
- Overlap: `DomainEntityInstance.HostAbi.cs` and `DomainEntityInstance.InvokeNamed.cs` are also touched by C8-tc1 (typed create, lane B). Keep edits to the exact `GetOrAnalyze` lines only, so the two diffs can be rebased past each other.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `TransitionStage` domain guard (`HostAbi.cs:141`) | `GetOrAnalyze(Domain)` | `Analysis(Domain)` | `StageTransitionHostAbiTests` |
| Notify body run (`ExecuteNotifyStageSubscribers`, `HostAbi.cs:313-315`) | `GetOrAnalyze` then 3-arg `GetOrLower`, analysis unused otherwise | `GetOrLower(Domain)` | `DomainEntityInstanceTests` notify rows; `WhenAnySimulatePrintAgreeTests` |
| Subscription effects (`ExecuteSubscriptionEffects`, `HostAbi.cs:363-365`) | same pair, analysis unused otherwise | `GetOrLower(Domain)` | `SubscriptionAnalysisTests`; `WhenAnySimulatePrintAgreeTests` |
| `When…` handler match (`InvokeNamed.cs:146`) | `GetOrAnalyze(Domain)` | `Analysis(Domain)` | `DomainEntityInstanceTests` subscription-invoke rows |
| Subscriber registry fields (`Runtime.cs:304`) | `GetOrAnalyze(domain)` | `Analysis(domain)` | `DomainEntityInstanceTests` notify rows |
| create-in storage target (`Store.cs:103`) | `GetOrAnalyze(domain)` | `Analysis(domain)` | `DomainInstanceHostJobsTests`; `DomainInstanceStoreFailClosedTests` |
| Link cardinality contract (`Store.cs:369`) | `GetOrAnalyze(source.Domain)` | `Analysis(source.Domain)` | `DomainInstanceStoreFailClosedTests` link rows |
| NotifyTransition catalog guard (`Store.cs:470`) | `GetOrAnalyze(domain)` | `Analysis(domain)` | `DomainInstanceStoreFailClosedTests` |
| Subscriber fields for link (`Store.cs:492`) | `GetOrAnalyze(domain)` | `Analysis(domain)` | `DomainEntityInstanceTests` subscription rows |
| Behavior metadata build (`BehaviorMetadata.cs:47`) | `GetOrAnalyze(domain)` | `Analysis(domain)` | `InfrastructureAnalyzerTests` |
| MCP oracle expression (`OracleTool.cs:420`) | `GetOrAnalyze(domain)` | `Analysis(domain)` | `OracleToolTests` |
| Analysis with Errors (G1) | either accessor returns the same result, `GetOrLower` throws first Error | unchanged | `CompileRefusesErrorsTests`; `DomainSemanticLookupFailClosedTests` |
| `Analysis` vs `GetOrAnalyze` identity | n/a | same cached `AnalysisResult` | new `Item3LowerPopulateTests` row |

## Done when
- `git grep -n GetOrAnalyze -- Poly/DomainModeling/Runtime` is empty (the card's condition).
- `git grep -n GetOrAnalyze -- Poly Poly.Mcp src` is the definition at `RuntimeAnalysisCache.cs:85` only (the two `DomainEntityInstance.HostAbi.cs` pairs are gone; the seven metadata sites plus `BehaviorMetadata.cs` and `OracleTool.cs` read `Analysis`).
- `RuntimeAnalysisCache.Index`, the 1-arg/3-arg `GetOrLower`, and `GetOrAnalyze` keep their current signatures; `CatalogIndex` is unchanged.
- The 12 test files that still call `GetOrAnalyze` compile and pass unchanged (they move in C8d).
- Suite green; fail-closed and refuse-on-errors rows unchanged.

## SHIP if / NOT SHIP if
SHIP if the Runtime grep is empty, product `GetOrAnalyze` is only the definition, the two `HostAbi` pairs read `GetOrLower(Domain)`, the seven metadata sites plus `BehaviorMetadata` and `OracleTool` read `Analysis`, `Index`/`CatalogIndex`/`GetOrAnalyze` signatures are unchanged, and the suite is green with the fail-closed and G1 refuse rows passing.

NOT SHIP if any Runtime site keeps `GetOrAnalyze`, if `CatalogIndex` grows a member or `Index`/`GetOrLower` changes signature, if a behavior test was edited to fit, if a new cache field or lowered-body table appears, or if `EffectLoweringPass.cs`, `DomainToCSharpExporter.StoreBind.cs`, `DomainEntityInstance.HostAbi.cs` typed-create hunks or `DomainEntityInstance.InvokeNamed.cs` typed-create hunks from lane B C8-tc1 appear in the diff.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Item3LowerPopulateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainInstanceStoreFailClosedTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainSemanticLookupFailClosedTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StageTransitionHostAbiTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SubscriptionAnalysisTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/WhenAnySimulatePrintAgreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/WhenAllSimulatePrintAgreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainInstanceHostJobsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/InfrastructureAnalyzerTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/OracleToolTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CompileRefusesErrorsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
One internal accessor plus nine one-line swaps and two pair collapses across four Runtime files, one Analysis file and one MCP file; Scot can open each `Analysis`/`GetOrLower` call site cold.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | opencode/opencode-go/deepseek-v4.1-flash | 78231090 | planned | - |
| 2026-10-10 | implementer | grok/grok-4.6 | 8a2ed7e9 | pushed | tests 3529/3529; sweep: Runtime GetOrAnalyze empty; product definition only |
