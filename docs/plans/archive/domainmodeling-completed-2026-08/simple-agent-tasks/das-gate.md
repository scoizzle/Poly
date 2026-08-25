# DAS Gate — Wave and suite checklist

**Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)

Status: `[x]` Wave 4 + suite closed 2026-07-31

## Always (every wave)

- [x] Pre-ship review on dirty tree (structure / contract / edge / hygiene).
- [x] `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` green.
- [x] `dotnet run --project Poly.Tests/Poly.Tests.csproj` green (or document known pre-existing failures unrelated to DAS).
- [x] CORE / future-state / this suite docs match the tree if mechanisms changed.
- [x] No new semantic dual path introduced without `DM-META-REMOVE-FALLBACK` **and** a W4 task reference. *(W4 end-state: markers = 0; do not reintroduce dual paths.)*

---

## Wave 0 — Projection boundary

- [x] G0.1: `EntitySyntaxPass` not registered in `UseDomainModelAnalysisPipeline` (or equivalent hard-off). *(W0.1)*
- [x] G0.2: DslCompiler / entity emit uses `DomainProgramProjection.ToSyntax(domain, analysis)` (or Export) on finished `AnalysisResult`. *(W0.2)*
- [x] G0.3: Projection failure fails loud (no silent skip of all entity files). *(W0.2)*
- [x] G0.4: Exporter helpers do not rely on `metadata as AnalysisResult` for `GetMetadata` (provider works for `AnalysisResult` at minimum; context adapter if still needed). *(W0.3)*
- [x] G0.5: Pipeline tests updated (no required mid-pipeline `EntitySyntaxMetadata` for “analysis complete”). *(W0.1 / W0.2 types deleted)*
- [x] G0.6: Progress notes on W0 tasks; `das-README` W0 status `[x]`. *(2026-07-30 after plan-orchestrator until-done)*

**Evidence (date / commands / notes):**

```
Wave 0 CLOSED 2026-07-30 (plan-orchestrator until-done: W0.2 + W0.3; W0.1 prior run):
- UseDomainModelAnalysisPipeline: no EntitySyntaxPass; export-time-only comment.
- DslCompiler.GenerateAllFiles: DomainProgramProjection.ToSyntax(domain, analysis);
  missing entity defs throw; Compile wraps as Fail("Code generation failed: …").
- EntitySyntaxPass + EntitySyntaxMetadata deleted (no mid-pipeline IR bag).
- MCP export_domain_to_csharp: DomainToCSharpExporter.Export → ToSyntax, catch→tool fail.
- INodeMetadataProvider GetMetadata paths; zero `as AnalysisResult` casts in **/*.cs.
- Export(Domain, AnalysisResult) public boundary kept; RLM fail-closed (F5) tests green.
- Scoped tests green on implement (Export_*, ResolveRelationship_*, ToSyntax_ViaProvider*,
  DomainAnalysis_*, DslCompiler_EntitiesMode_EmitsEntityTypesFromProjection).
- Residual dual-path scans (DM-META-REMOVE-FALLBACK) deferred to W4 — not W0 scope.
- Always: pre-ship on full dirty tree + full suite re-run still recommended before merge
  of the whole DAS batch; Wave 0 product ACs met.
```

---

## Wave 1 — Catalog

- [x] G1.1: Catalog design doc or ADR subsection accepted (keying, slices, SA ownership).
- [x] G1.2: Single publisher produces catalog on successful analyze.
- [x] G1.3: Lookup extensions / evolution / MCP describe / runtime action resolve consume catalog (or dual-write with documented primary).
- [x] G1.4: Duplicate index owners retired or dual-write ended (no three sources of truth).
- [x] G1.5: Fail-closed tests for missing catalog when analysis present.
- [x] G1.6: `das-README` W1 status `[x]`.

**Evidence:**

```
W1 CLOSED after W1.4 verify (severity none):
- docs/plans/das-catalog-design.md ownership matrix + CORE §3.1 catalog note
- DomainCatalogPass sole SetMetadata DomainCatalogMetadata; sole new ARM/MTI (embedded)
- RuntimeContractAnalyzer only SetMetadata RCM (default) + SDP (stage)
- Grep: zero production GetMetadata<ActionResolutionMetadata|MutationTargetIndexMetadata>
- DomainSemanticLookupExtensions domain-keyed catalog-only; Evolution.GetMutationIndex throw if missing
- Oracle DescribeAction/DescribePolicy → GetActionResolution/GetMutationIndex
- Tests: RuntimeContractMetadataTests, PipelineMergeMetadataTests, DomainSemanticLookupFailClosedTests
  assert ARM/MTI null after analyze
- Full suite not re-run in read-only verifier; AC3 backed by contract tests only
```

---

## Wave 2 — Effective surface

- [x] G2.1: One algorithm for effective policies/actions at a stage.
- [x] G2.2: MCP DescribeStage / helpers align with that algorithm.
- [x] G2.3: Behavior is adapter or deleted; no third composition path.
- [x] G2.4: Capability transition targets use real stages from catalog (no empty stub stages).
- [x] G2.5: `das-README` W2 status `[x]`.

**Evidence:**

```
W2 product closed after W2.1 verify (severity none):
- DomainEffectiveSurface owns compose (entity+stage policies; stage-local actions; no action policies).
- CapabilityAnalyzer publishes StageCapabilityMetadata via compose; deps Semantic + DomainCatalogPass.
- GetEffectivePolicies/GetEffectiveActions → TryGetStage fail-closed, then StageCapability
  first, catalog compose fallback (symmetric unknown-stage empty — no entity-policy vacuous path).
- SemanticDomainAnalyzer.PublishEffectivePolicies → ComposeStagePolicies (same algorithm).
- OracleTool.DescribeStage → helpers only when LatestAnalysis set.
- BehaviorPass thin DTO adapter; BuildBehavior → DomainModelAnalyzer (no effect-walk dual path).
- Transition targets: catalog StagesByEntity real Stage refs (no empty stubs).
- Tests: GetEffectivePolicies_UnknownStage_ReturnsEmpty_NotEntityPolicies (cap+catalog),
  GetEffectiveActions_UnknownStage_ReturnsEmpty, multi-policy exclude action policies,
  ActionCapability real Stage refs, DescribeStage_EffectiveCounts_MatchHelpers.
- CORE §3.1 W2 effective-surface note present; das-README W2 [x].
- Read-only verifier: source review only (no re-run of git/dotnet); no residual dual compose found.
```

---

## Wave 3 — Validate / deps

- [x] G3.1: Fact-publishing passes declare accurate `Dependencies`. *(W3.1)*
- [x] G3.2: Lint-only passes labeled as such (no silent metadata consumers depend on undeclared order). *(W3.1)*
- [x] G3.3: Effect/Policy megapass split started or scoped: fact emitters vs diagnostic packs (progress notes OK if multi-PR). *(W3.2)*
- [x] G3.4: `das-README` W3 status `[x]`. *(W3.2)*

**Evidence:**

```
W3.1 CLOSED 2026-07-31 (verify pass, severity nit):
- Fact publishers/consumers declare Dependencies (Capability→Catalog+Semantic;
  Ownership→Topology+EntityStructure; Storage/Transport→Topology+Ownership;
  CrossReference→Topology; EntityStructure→Semantic; lint readers declare bag publishers).
- Empty deps only: Structural, ContractIntegration (lint), Semantic, ConstraintPropagation,
  EffectTopology (justified). Catalog omits RuntimeContract deliberately.
- DomainModelAnalyzer: ConstraintPropagation registered before Effect.
- PassDependencyDeclarationTests: known deps, telemetry order edges, AnalyzerBuilder missing-dep throw.

W3.2 CLOSED 2026-07-31 (implement green; static AC verify severity nit):
- RequiredPropertiesPass (DomainRequiredProperties) → RequiredPropertiesMetadata;
  PolicyConstraintAnalyzer zero SetMetadata (lint-only).
- EffectFactsPass (DomainEffectFacts) → ResolvedRelationshipTargetMetadata via TryResolveCreateIn;
  EffectAnalyzer zero SetMetadata; deps Semantic+RequiredProperties+ConstraintPropagation (not EffectFacts).
- DomainModelAnalyzer: fact emitters then validate packs.
- Consumers: RuleCoverageAnalyzer + EffectAnalyzer unsatisfied-req → RequiredProperties;
  EffectLoweringPass still GetMetadata ResolvedRelationshipTargetMetadata.
- Tests (implement): ValidationFactsSplitTests (3), PassDependencyDeclarationTests order/deps,
  PipelineMergeMetadataTests required bag; targeted effect/policy suites green.
- CORE §3.1 W3.2 note; das-w3-2 inventory + severity tiers + Effect ~1k LOC follow-ups;
  dual create-in resolve documented (both correct). Full suite not re-run in static verify.
- das-README W3 [x]; G3.3/G3.4 satisfied. Wave 3 product ACs met; optional gate ceremony only.
```

---

## Wave 4 — Zero dual paths

- [x] G4.1: Runtime semantic routes: no `DM-META-REMOVE-FALLBACK` when `Domain` bound. *(W4.1 — verify 2026-07-31 pass, nit)*
- [x] G4.2: MCP describe / lowering / export semantic routes: no residual soft-scans when analysis present. *(W4.2 + W4.3 re-open fix: EffectLowering ESM ctor order fail-closed under analysis)*
- [x] G4.3: Workspace grep of `DM-META-REMOVE-FALLBACK` in DomainModeling + OracleTool + MinimalApi (+ all `*.cs`) is **0**. *(W4.3 implement + verify confirmed)*
- [x] G4.4: DACR suite Done Definition item 4 closed / superseded by DAS W4 evidence.
- [x] G4.5: `das-README` W4 + suite Done Definition complete.

**Evidence:**

```
W4.1 / G4.1 CLOSED 2026-07-31 (implement success; verify pass, severity nit):
- Grep: zero DM-META-REMOVE-FALLBACK in DomainEntityInstance.cs and DomainInstanceStore.cs.
- Domain-bound InvokeActionInternal: GetActionResolution required (throws if null) then
  TryResolveAction(Domain, …); stage guards require ESM; TransitionStage uses TryGetStage
  when analysis present, Entity.Stages only when Domain==null.
- CreateChildInstance / create-in / outbound relationships: TryGetEntity/TryGetRelationship
  fail-closed; NotifyTransition early-returns Domain==null else RCM/ESM/SDPM fail-closed
  (no Domain.Relationships scan).
- Standalone reduced contract: type remarks + ResolveStandaloneAction; tests
  InvokeAction_DomainBound_* / Standalone_* + DomainInstanceStoreFailClosedTests.

W4.2 product CLOSED 2026-07-31 (implement success; verify pass, severity suggestion):
- rg DM-META-REMOVE-FALLBACK: 0 in OracleTool.cs, DomainModeling/Lowering/,
  DomainModeling/Evolution/.
- OracleTool DescribeStage/Action/Policy/Relationship require LatestAnalysis;
  catalog/ESM-backed; missing-metadata ≠ not-found.
- EffectLoweringPass StageTransition TryGetStage when analysis present;
  create-in requires analysis.
- DomainToCSharpExporter CreateNav/ESM ctor params throw; enum/relationship
  analysis-present fail closed.
- DomainMutationContext catalog-first + single live-overlay.
- Tests: DomainSemanticLookupFailClosedTests Describe* pairs + ResolveRelationship RLM.

W4.3 CLOSED 2026-07-31 (implement re-open fix after verify fail; re-verify pass suggestion):
- MinimalApiGenerator: GetConstructorOrder requires EntityStructureMetadata (throw);
  create/seed monopath; Create_MissingEntityStructureMetadata_Throws present.
- EffectLoweringPass.GetConstructorParameterOrder: analysis present → ESM required
  (throw if missing); return ConstructorParameters as-is (no property-order rebuild).
  Analysis absent → structural rebuild only (standalone non-goal; not analysis-present dual path).
- Sibling monopaths: DomainToCSharpExporter.GetConstructorParameters + MinimalApi GetConstructorOrder
  both throw without ESM.
- Tests: EffectLowering_MissingEntityStructureMetadata_Throws + Create_MissingEntityStructureMetadata_Throws.
- rg DM-META-REMOVE-FALLBACK **/*.cs = 0.
- DACR item 4 / G2 / F33 closed via DAS W4 evidence; dacr-followups header F33 drift fixed on record.
- Commands: dotnet build Poly.Benchmarks (0/0); full suite 1762/0 implementer-only (verify no re-run).
```

---

## Suite complete

- [x] All wave gates above checked.
- [x] Future-state §11 success picture reviewed; each item met or deferred with link.
- [x] CORE still points at future-state + inventory accurately.
