# TASK C7a - Six 'get compiled module' sites use the catalog
Status: merged #150 `3b53f0fa` 2026-10-10. Branch slice/c7a-plan-v2. Lane B. Review 2. Implement mill: Grok (review on OpenCode).
Plan card: [`pipeline-convergence-plan.md` **C7a**](../../../domain-modeling/pipeline-convergence-plan.md) (`:545-552`). Depends on C7-0, merged `d344de77`.

## Scope
The six runtime sites that pair `RuntimeAnalysisCache.GetOrAnalyze(domain)` with the 3-arg `RuntimeAnalysisCache.GetOrLower(domain, Session(domain), analysis)` all live in `Poly/DomainModeling/Runtime/DomainEntityInstance.cs`: `RunCreateFactoryChecks` `:180-182`, `EvaluatePolicy` `:455-456`, `InvokeActionInternal` `:663-664`, `ExecuteEffectList` action branch `:815-816` and stage branch `:824-825`, and `ModuleAwareTypeProvider` `:919-921`. Each feeds the analysis to `GetOrLower` only to obtain the compiled module (the cache's artifact list). The compile-side tree catalog and its `CatalogIndex` (`Compile/CatalogIndex.cs`, C7-0) are the lookup surface C7b first consumes; C7a adds no index consumer. This slice replaces the six analyze-then-lower pairs with one `GetOrLower(Domain)` accessor that lowers from the cached analysis once, so the six sites hold the artifact list instead of re-reading analysis. `EnsureAnalysis` is the existing `GetOrAnalyze` body factored out so the accessor adds no `GetOrAnalyze` call site.

## Files
- `Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs` — add `public static IReadOnlyList<TypeDefinitionNode> GetOrLower(Domain domain)`; move the body of `GetOrAnalyze` (`:82-96`) into `private static AnalysisResult EnsureAnalysis(Domain domain)` and delegate both `GetOrAnalyze` and the new overload to it.
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` — replace the six pairs (`:180-182`, `:455-456`, `:663-664`, `:815-816`, `:824-825`, `:919-921`) with a single `RuntimeAnalysisCache.GetOrLower(domain)` / `(Domain)` call.
- `Poly.Tests/DomainModeling/Compile/Item3LowerPopulateTests.cs` — add one row next to `GetOrLower_Twice_ReturnsSameModuleReference` (`:228-241`) proving the 1-arg overload is reference-equal to the 3-arg call after one lower.
- Named and left alone (still hold `GetOrAnalyze`; C7b-d own them): `Runtime/DomainInstanceStore.cs`, `Runtime/DomainEntityInstance.HostAbi.cs`, `Runtime/DomainEntityInstance.InvokeNamed.cs`, `Runtime/DomainEntityInstance.Runtime.cs`, `Analysis/BehaviorMetadata.cs`, `Compile/CatalogIndex.cs`, `Compile/DomainSession.cs`.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Domain-bound Create with a violated constraint (`RunCreateFactoryChecks`) | `GetOrAnalyze(domain)` then 3-arg `GetOrLower` (`DomainEntityInstance.cs:180-182`) | `GetOrLower(domain)` returns the same module (`:181`) | `DomainEntityInstanceTests` create/constraint rows |
| `EvaluatePolicy` on a Domain-bound instance | `GetOrAnalyze` + 3-arg `GetOrLower` (`:455-456`), then `TryGetPolicyBody` | one `GetOrLower(Domain)` (`:455`), same body | policy rows in `DomainEntityInstanceTests` / `ParityTests` |
| Action dispatch guard prelude (`InvokeActionInternal`) | `ensureAnalysis` local fed to `GetOrLower` (`:663-664`) | one `GetOrLower(Domain)` (`:663`) | `DomainEntityInstanceTests` action-guard rows |
| Named-action effect execution (`ExecuteEffectList` action branch) | `GetOrAnalyze` + `GetOrLower` (`:815-816`) | one `GetOrLower(Domain)` (`:815`) | `ParityTests` action rows |
| Entry/exit effect execution (`ExecuteEffectList` stage branch) | `GetOrAnalyze` + `GetOrLower` (`:824-825`) | one `GetOrLower(Domain)` (`:824`) | `StageTransitionHostAbiTests` |
| Module-aware type provider (`ModuleAwareTypeProvider`) | `GetOrAnalyze` + `GetOrLower` (`:919-921`) | `GetOrLower(domain)` list (`:919`) | action/policy rows in `DomainEntityInstanceTests` |
| Domain whose analysis has Errors (G1) | the pair throws the first error | `GetOrLower(domain)` still throws the first error | `CompileRefusesErrorsTests`, simulator-refuses rows in `DomainEntityInstanceTests` |
| 1-arg vs 3-arg module identity | n/a | reference-equal after one lower | new row in `Item3LowerPopulateTests` |

## Done when
- The six `DomainEntityInstance.cs` sites call `RuntimeAnalysisCache.GetOrLower(Domain)` and no longer call `GetOrAnalyze`.
- `git grep -c GetOrAnalyze` over product code totals 12, down 6 from 18; `Runtime/DomainEntityInstance.cs` reads 2 (its remaining `:99` and `:612` are C7b/C7c).
- Suite unchanged; `CompileRefusesErrorsTests` and the simulator-refuses rows in `DomainEntityInstanceTests` still pass.
- No new cache accessor or lookup path beyond the one overload; no `CatalogIndex` consumer added.

## SHIP if / NOT SHIP if
SHIP if the product `GetOrAnalyze` count drops by exactly 6, the six sites read the module through the single `GetOrLower(Domain)` overload, and the suite is green with G1's refuse tests passing.

NOT SHIP if any of the six sites adds a new cache read (a second analysis lookup or lower path), if a behavior test changed to accommodate the refactor, or if a `CatalogIndex` consumer appears (that is C7b).

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Item3LowerPopulateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CompileRefusesErrorsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StageTransitionHostAbiTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CatalogIndexTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
One overload plus one body extraction in `RuntimeAnalysisCache.cs` and ten one-line call replacements in `DomainEntityInstance.cs`; Scot can read the whole diff cold.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | opencode/opencode-go/deepseek-v4.1-flash | d344de77 | planned | - |
| 2026-10-10 | implementer | grok/grok-4.6 | 46b75945 | pushed | tests 3514/3514; sweep: six pairs gone; HostAbi pair listed |
| 2026-10-10 | planner | grok/grok-4.6 | 3b53f0fa | merged #150 | - |
