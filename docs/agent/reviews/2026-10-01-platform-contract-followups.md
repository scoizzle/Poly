# Platform contract follow-ups — 2026-10-01

Source review: [`2026-10-01-platform-contract-review.md`](2026-10-01-platform-contract-review.md).

No suite is CURRENT (`docs/plans/simple-agent-tasks/PIPELINE-STATUS.md`). These tasks are the work queue. F1–F7 are closed. F8 stays open. Do not mark the frozen contract closed while F8 is open.

Evidence for F1–F6: `dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false` — 2900 succeeded, 0 failed (2026-10-01).

## Prior items

- **p3b action- and stage-scoped policies are not module methods** — closed as F1. Entity, stage, and action-local definitions each emit one bool method. A second definition of the same name is a structural error. Domain-bound invoke no longer re-checks stage policies; Domain-null still does.
- **p3b execute-time `DomainExpressionLoweringPass` sites** — #94 deleted `EvaluateParameterBindings`, `BindPeerInEffect`, and `EvaluateExprOnPeer` (F7). `PrevalidateCreateInitializers` and `CreateChildInstance` still lower, and the live callers pass literal bindings. Anchors are in `docs/plans/p3b-followups-2026-09-27.md`. That residual is not CURRENT.
- **PIPELINE-STATUS unbound fallback** — closed as F3. `GetHolder` calls `DomainSession.ForExtensions` (Core). An id that catalog cannot load throws. `DomainSession.Analyze` binds the compiling session before the pipeline, so a sqlite compile does not build a second Core session.

## Tasks

- [x] **F1** — Stage and action-local policies are bool methods on the operation module (`DomainToCSharpExporter.cs` `AddPolicyMethod`, `EmitStagePolicyGuards`). Domain-bound invoke does not re-check them. Test: `Lower_StagePolicy_IsMethodAndActionGuard`.
- [x] **F2** — Same policy name on entity, stage, or action with a different expression is `StructuralDuplicate`. `require` copies (`ReferenceEquals`) are uses. Exporter throws if a second method of that name is added. Test: `Policy_SameNameOnEntityAndStage_IsRedefinition`.
- [x] **F3** — Unbound `GetHolder` calls `DomainSession.ForExtensions` and throws when Core cannot load the id. `Analyze` binds the compiling session first. Test: `GetOrAnalyze_UnboundUnknownExtension_Throws`. `Analyze_WithSqlite_GetOrAnalyzeSeesSqliteMaps` still sees TEXT → TEXT.
- [x] **F4** — `DomainEvolution.Apply` throws when a passed session has not loaded a proposed extension id, including ids inside Core. Test: `AddDomainExtension_SessionHasNotLoadedId_Throws` (`storage` on a temporal-only session). `DslCompiler` rewrites source `uses persistence` to the vendor id that session linked.
- [x] **F5** — `DomainSession.Lower` throws when `analysis.HasErrors`, with the error text. `Emit` calls `Lower`. Test: `Lower_WhenAnalysisHasErrors_Throws`.
- [x] **F6** — Entry/exit lookup is `OnEntry{stage}` / `{stage}OnEntry` and the exit pair. No bare `OnEntry` / `OnExit`.
- [x] **F7** — #94 deleted the uncalled helpers `EvaluateParameterBindings`, `BindPeerInEffect`, and `EvaluateExprOnPeer`. The two live literal re-lowers stay in `docs/plans/p3b-followups-2026-09-27.md` and are not CURRENT.
- [ ] **F8** — `GetHolder` now calls `ForExtensions`. Still open: `AddDomainExtensionChange` seeds primitives through `ExtensionCatalog.Core.Contains` on its own, and there is no separate null-session sibling test. Policy names share one method namespace (F2) rather than a scope-qualified key.
