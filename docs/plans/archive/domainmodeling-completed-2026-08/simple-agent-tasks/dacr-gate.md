# DACR Gate - Phase Completion Checklist

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Status: [x] Helpers + tagging complete; r1–r5 F1–F31 resolved; Done Definition item 4 closed via DAS W4.3

## Goal

Prevent partial completion claims by enforcing semantic-contract and fail-closed checks at each phase boundary.

## Gate Checks

- [x] G1: Scoped semantic routes require AnalysisResult.
  - DomainToCSharpExporter.Export(Domain, AnalysisResult) requires non-nullable.
  - DomainProgramProjection.ToSyntax(Domain, AnalysisResult) requires non-nullable.
  - OracleTool.analyze_effect/lower_effect_to_csharp check LatestAnalysis and fail closed.
  - DomainInstanceStore.NotifyTransition throws when required metadata is missing.
  - EffectLoweringPass.CreateEntityInRelationship throws when _analysis is null.
- [x] G2: Metadata-first path is primary; dual-path fallbacks removed (DAS W4) — **closed 2026-07-31**.
  - Markers: `rg DM-META-REMOVE-FALLBACK` over `**/*.cs` = **0** (W4.3).
  - Fail-closed monopaths: runtime, MCP describe, export, evolution, MinimalApi, EffectLowering ctor order under analysis (W4.1–W4.3).
  - `EffectLoweringPass.GetConstructorParameterOrder`: analysis present → ESM required (throw); no property-order rebuild. Analysis-null structural rebuild retained as standalone only.
  - Evidence: [`das-w4-3-marker-zero-and-dacr-close.md`](./das-w4-3-marker-zero-and-dacr-close.md), [`das-gate.md`](./das-gate.md) G4.2.
- [x] G3: Missing analysis and missing required metadata fail closed.
  - DomainInstanceStore.NotifyTransition: throws when RelationshipContractMetadata or EntityStructureMetadata is missing for live subscribers; SubscriptionDispatchPlanMetadata also required per subscriber stage.
  - EffectLoweringPass.CreateEntityInRelationship: throws when _analysis is null.
  - DomainToCSharpExporter.BuildTypeDefsForEntity: ArgumentNullException on null metadata.
  - Poly/DomainModeling/Lowering/DomainToCSharpExporter.ResolveRelationship: throws when analysis present but RelationshipLookupMetadata absent (F5).
  - OracleTool.analyze_effect/lower_effect_to_csharp: explicit error when LatestAnalysis is null.
  - OracleTool describe routes (DescribeStage/DescribeAction/DescribePolicy/DescribeRelationship): when analysis present, return not-found without soft-scan fallback (F4).
  - DomainEntityInstance.InvokeActionInternal: when analysis ran but TryResolveAction/TryGetStage returns null, skip scan / fail closed (F2); stage-guard lookup miss throws (F17); analysis-absent scan path preserves SA fallthrough predicate (F24, B-2); ESM-absent vs stage-not-found throws distinguished (F25a); present-but-soft structural scans removed (F26).
  - RuntimeAnalysisCache.GetOrAnalyze: always returns analysis or fails.
  - All follow-ups F1–F23 resolved in r1–r4; r5 (F24–F31) resolved.
- [x] G4: Structural traversals retained are projection-only.
  - BuildSnapshot (DomainTools): structural entity/relationship enumeration.
  - DescribeEntity/DescribeStage/DescribeAction/DescribePolicy: structural entity enumeration with metadata-backed semantic resolution.
  - DomainProgramProjection.ToSyntax: structural domain traversal for type definition rendering.
  - Evolution handlers using UpdateEntity/UpdateStage/UpdateAction: mutation through context methods, not semantic rediscovery.
- [x] G5: Build and tests are green.
  - dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj: 0 errors, 0 warnings.
  - dotnet run --project Poly.Tests/Poly.Tests.csproj: 1728 passed, 0 failed.

## Evidence Log

Phase: All (P0-P6 + Gate)
Date: 2026-07-30
Changed files:
  - Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs (NEW)
  - Poly.Tests/DomainModeling/Analysis/DomainInstanceStoreFailClosedTests.cs (NEW)
  - Poly.Tests/DomainModeling/Analysis/DomainSemanticLookupFailClosedTests.cs (NEW)
  - Poly/DomainModeling/DomainEntityInstance.cs (5 DM-META-REMOVE-FALLBACK tags; r5: SA fallthrough in scan path F24, stage-guard throw split F25a, soft scans removed F26)
  - Poly/DomainModeling/Evolution/DomainMutationContext.cs (5 DM-META-REMOVE-FALLBACK tags)
  - Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs (10 DM-META-REMOVE-FALLBACK tags)
  - Poly/DomainModeling/Lowering/EffectLoweringPass.cs (7 DM-META-REMOVE-FALLBACK tags)
  - Poly.Mcp/Tools/OracleTool.cs (4 DM-META-REMOVE-FALLBACK tags; metadata-first describe routes)
  - src/Poly.DslCompiler/MinimalApiGenerator.cs (3 DM-META-REMOVE-FALLBACK tags)
  - docs/plans/simple-agent-tasks/dacr-*.md (status updates + progress notes)
Tests run: 1728 total (1728 passed, 0 failed)
Remaining risks:
  - Standalone (`Domain == null`) runtime keeps a reduced contract (DAS non-goal; not full peer of domain-bound).
  - Follow-ups F1–F31 resolved; F33 closed via DAS W4.3; F34 optional hygiene may remain.

### DAS W4 / item 4 (2026-07-31) — closed

- W4.1: DomainEntityInstance / DomainInstanceStore — zero markers; domain-bound catalog-only.
- W4.2: OracleTool, export, evolution — markers 0; analysis-present fail-closed on scoped routes.
- W4.3: MinimalApiGenerator ESM constructor order required; EffectLowering ctor order fail-closed under analysis; workspace `*.cs` markers **0**; full suite 1762/0.
- Pointers: [`das-gate.md`](./das-gate.md) G4.1–G4.5, [`das-w4-3-marker-zero-and-dacr-close.md`](./das-w4-3-marker-zero-and-dacr-close.md).
