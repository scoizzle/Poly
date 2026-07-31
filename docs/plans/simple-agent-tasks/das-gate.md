# DAS Gate — Wave and suite checklist

**Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)

Status: `[ ]` Not complete

## Always (every wave)

- [ ] Pre-ship review on dirty tree (structure / contract / edge / hygiene).
- [ ] `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` green.
- [ ] `dotnet run --project Poly.Tests/Poly.Tests.csproj` green (or document known pre-existing failures unrelated to DAS).
- [ ] CORE / future-state / this suite docs match the tree if mechanisms changed.
- [ ] No new semantic dual path introduced without `DM-META-REMOVE-FALLBACK` **and** a W4 task reference.

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

- [ ] G1.1: Catalog design doc or ADR subsection accepted (keying, slices, SA ownership).
- [ ] G1.2: Single publisher produces catalog on successful analyze.
- [ ] G1.3: Lookup extensions / evolution / MCP describe / runtime action resolve consume catalog (or dual-write with documented primary).
- [ ] G1.4: Duplicate index owners retired or dual-write ended (no three sources of truth).
- [ ] G1.5: Fail-closed tests for missing catalog when analysis present.
- [ ] G1.6: `das-README` W1 status `[x]`.

**Evidence:**

```
(empty until run)
```

---

## Wave 2 — Effective surface

- [ ] G2.1: One algorithm for effective policies/actions at a stage.
- [ ] G2.2: MCP DescribeStage / helpers align with that algorithm.
- [ ] G2.3: Behavior is adapter or deleted; no third composition path.
- [ ] G2.4: Capability transition targets use real stages from catalog (no empty stub stages).
- [ ] G2.5: `das-README` W2 status `[x]`.

**Evidence:**

```
(empty until run)
```

---

## Wave 3 — Validate / deps

- [ ] G3.1: Fact-publishing passes declare accurate `Dependencies`.
- [ ] G3.2: Lint-only passes labeled as such (no silent metadata consumers depend on undeclared order).
- [ ] G3.3: Effect/Policy megapass split started or scoped: fact emitters vs diagnostic packs (progress notes OK if multi-PR).
- [ ] G3.4: `das-README` W3 status `[x]`.

**Evidence:**

```
(empty until run)
```

---

## Wave 4 — Zero dual paths

- [ ] G4.1: Runtime semantic routes: no `DM-META-REMOVE-FALLBACK` when `Domain` bound.
- [ ] G4.2: MCP describe / lowering / export semantic routes: no residual scans when analysis present.
- [ ] G4.3: Workspace grep of `DM-META-REMOVE-FALLBACK` in DomainModeling + OracleTool (+ agreed scope) is **0** or only non-semantic documented exceptions with ADR.
- [ ] G4.4: DACR suite Done Definition item 4 closed or explicitly superseded by DAS W4 evidence.
- [ ] G4.5: `das-README` W4 + suite Done Definition complete.

**Evidence:**

```
(empty until run)
```

---

## Suite complete

- [ ] All wave gates above checked.
- [ ] Future-state §11 success picture reviewed; each item met or deferred with link.
- [ ] CORE still points at future-state + inventory accurately.
