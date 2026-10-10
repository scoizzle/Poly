# TASK C7c - Entity and relationship lookups use index and navigation
Status: planned. Branch slice/c7c-plan. Lane A. Review 1. Implement mill: Grok (review on OpenCode).

## Scope
Card `**C7c**` (`docs/domain-modeling/pipeline-convergence-plan.md:564-571`; Depends on C7b, merged `67427b72`) is the entity/relationship member of the C7-0..C7d lookup group. C7b added `RuntimeAnalysisCache.Index(Domain)` (`Analysis/RuntimeAnalysisCache.cs:110-116`): `EnsureAnalysis` + `RequireCatalog`, `session.Lower` so `Session.ArtifactCatalog` fills, then `new CatalogIndex(session.ArtifactCatalog)`; `CatalogIndex.Tree`/`Navigation` (`Compile/CatalogIndex.cs:29-58`) answer entity-by-name and member-by-name. The card counted 4 entity + 3 relationship lookups; two relationship ones sat in C0's deleted helpers (`GetOutboundRelatedInstances`, `TryEvaluateRelationshipPresence`, removed `ab0772cd`). The live group on master is five sites: Create's G1 gate (`DomainEntityInstance.cs:94-101`), `TryResolveTargetEntity` (`DomainInstanceStore.cs:223-244`), `NavigationsFor` (`DomainEntityInstance.Runtime.cs:347-355`), `MapDomainTypeToAstNode` (`:517-521`), `ResolveSourceRelationshipOrThrow` (`:398-416`). Three still call `GetOrAnalyze` (`:101`, `:231`, `:402`); the other two scan `domain.Types` directly. This slice moves the name questions onto the index; ontology objects (`Entity`, `Relationship`) still come from the domain — the index carries `Artifact`/`PropertyDefinitionNode` only. The nine other `Runtime` `GetOrAnalyze` sites (subscription fan-out, stage names, storage target, link contract, `BehaviorMetadata`) are C7d. Lane B is on C8-tc (PR 155, `EffectLoweringPass` / `StoreBind.cs`); this touch is `Runtime/*` plus one test file, no overlap.

## Files
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` — `Create` `:94-101`: keep the canonical-entity scan `:96-98`; replace `ThrowIfHasErrors(GetOrAnalyze(domain))` with `GetOrLower(domain)` (throws the first Error, `RuntimeAnalysisCache.cs:122`).
- `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` — `TryResolveTargetEntity` `:223-244`: drop `GetOrAnalyze` + `analysis.TryGetEntity` (`:230-236`); presence via `Index(creator.Domain).Tree(typeName)`; the `Entity` from `creator.Domain.Types.OfType<Entity>()` by name.
- `Poly/DomainModeling/Runtime/DomainEntityInstance.Runtime.cs` — `ResolveSourceRelationshipOrThrow` `:398-416`: drop `GetOrAnalyze` + `GetRelationshipLookup` (`:400-408`); presence via `Index(Domain).Navigation(Entity.Name, DomainToCSharpExporter.ToPascalCase(relationshipName))`; the `Relationship` from `NavigationsFor(Entity, Domain)` by nav name; keep the "Declared on:" elsewhere message by scanning `Domain.Types` entities on the miss path. `MapDomainTypeToAstNode` `:517-521`: `Index(domain).Tree(typeName) is not null` replaces the `domain?.Types.OfType<Entity>().Any` scan.
- `Poly.Tests/DomainModeling/Compile/Item3LowerPopulateTests.cs` — one row next to `Index_AfterGetOrLowerOnly_MethodMatchesTryGetModuleMethod` `:257-273`: after one `GetOrLower(domain)`, `Index(domain).Tree("Patron")` and `Index(domain).Navigation("Patron","Fines")` are the catalog artifacts' nodes.
- Named and left alone: `NavigationsFor` `:347-355` (hands back the domain live entity's `Relationship` list — the index has no `Entity` payload — and feeds `MatchOneToOneNavigation` `:422` / `MatchCollectionNavigation` `:468` / `BuildTypeDefNode` `:211`, hot paths), Create's canonical scan `:96-98`, `Compile/CatalogIndex.cs` surface, `Analysis/RuntimeAnalysisCache.cs` `Index`, `Runtime.cs:304`, `HostAbi.cs:141/313/363`, `InvokeNamed.cs:146`, `DomainInstanceStore.cs:103/365/466/488`, `Analysis/BehaviorMetadata.cs:47`, `Poly.Mcp/Tools/OracleTool.cs` — all C7d.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Domain-bound `Create` on a domain with Errors (G1) | `ThrowIfHasErrors(GetOrAnalyze(domain))` `:101` | `GetOrLower(domain)` throws the same first Error | `CompileRefusesErrorsTests.Create_OnDomainWithErrors_Throws` `:67`; `DomainEntityInstanceTests` create rows |
| Create-in target type (`TryResolveTargetEntity`) | `GetOrAnalyze` + `analysis.TryGetEntity` `:230-236` | `Index(creator.Domain).Tree(typeName)` present; canonical `Entity` from `domain.Types` by name | `DomainEntityInstanceTests` create-in rows; `ActionEntityReturnTests` |
| Relationship by (source entity, name) (`ResolveSourceRelationshipOrThrow`) | `GetOrAnalyze` + `GetRelationshipLookup` + `TryGetRelationship` + `FindByNameAcrossSources` `:400-415` | `Index(Domain).Navigation(Entity.Name, ToPascalCase(name))`; `Relationship` from `NavigationsFor`; miss path keeps the "Declared on:" scan | `SameNameRelationshipSourceScopingTests.Runtime_LinkAndQuantifierPolicy_AreSourceScoped`; `DomainEntityInstanceTests` relationship rows |
| Domain type is an entity (`MapDomainTypeToAstNode`) | `domain?.Types.OfType<Entity>().Any(...)` `:519` | `Index(domain).Tree(typeName) is not null` | `DomainEntityInstanceTests` typed-action/return rows |
| `Index` after `GetOrLower` only | `ArtifactCatalog` empty when only `GetOrLower(domain)` ran | `Index` Lowers; `Tree`/`Navigation` are the catalog nodes | new `Item3LowerPopulateTests` row |
| Analysis with Errors (G1) | cache throws first error | `Index`/`GetOrLower` still throw it | `CompileRefusesErrorsTests`; simulator-refuses rows |

## Done when
- The group's three direct calls are gone: `git grep -n "GetOrAnalyze" -- Poly/DomainModeling/Runtime` reads 9 (was 12); over `Poly` 12 hits (was 15, including the definition at `RuntimeAnalysisCache.cs:82`).
- `TryResolveTargetEntity` and `ResolveSourceRelationshipOrThrow` resolve through `Index(...).Tree` / `.Navigation`; `MapDomainTypeToAstNode` answers with `Tree`; `Create` uses `GetOrLower`.
- `git grep -n "CatalogIndex" -- Poly` is `Compile/CatalogIndex.cs`, `RuntimeAnalysisCache.cs:115`, `DomainEntityInstance.cs:594` and the new `Runtime/*` callers; `CatalogIndex`'s surface is unchanged.
- Suite green; G1 refuse tests, source-scoping rows and the StageRequired wording unchanged.

## SHIP if / NOT SHIP if
SHIP if the three group `GetOrAnalyze` calls are gone, the four sites read `Index(Domain)` (`Tree`/`Navigation`), `CatalogIndex` is unchanged, and the suite is green with the G1 refuse and source-scoping rows passing.

NOT SHIP if a site keeps its `GetOrAnalyze` or re-scans `domain.Types` for the name question, if `CatalogIndex` grows a member or `Index` changes signature, if a behavior test was edited to fit, if `NavigationsFor` / `Match*` / Create's canonical scan were rewritten (out of scope), if the C7d sites (`HostAbi` `:141/:313/:363`, `InvokeNamed` `:146`, `Runtime.cs:304`, `Store` `:103/:365/:466/:488`, `BehaviorMetadata`, `OracleTool`) moved, or if `EffectLoweringPass.cs` / `StoreBind.cs` appear in the diff (Lane B C8-tc overlap).

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CatalogIndexTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Item3LowerPopulateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SameNameRelationshipSourceScopingTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ActionEntityReturnTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CompileRefusesErrorsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
Four one-call replacements plus one kept-scan note across two runtime files and one store method; Scot can open `Index`, `CatalogIndex.Tree`/`Navigation` and each lookup cold.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | opencode/opencode-go/deepseek-v4.1-flash | 60089ef0 | planned | - |
