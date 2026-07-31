# DACR Gate - Phase Completion Checklist

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Status: [x] Helpers + tagging complete; r1–r5 follow-ups F1–F31 resolved

## Goal

Prevent partial completion claims by enforcing semantic-contract and fail-closed checks at each phase boundary.

## Gate Checks

- [x] G1: Scoped semantic routes require AnalysisResult.
  - DomainToCSharpExporter.Export(Domain, AnalysisResult) requires non-nullable.
  - DomainProgramProjection.ToSyntax(Domain, AnalysisResult) requires non-nullable.
  - OracleTool.analyze_effect/lower_effect_to_csharp check LatestAnalysis and fail closed.
  - DomainInstanceStore.NotifyTransition throws when required metadata is missing.
  - EffectLoweringPass.CreateEntityInRelationship throws when _analysis is null.
- [x] G2: Metadata-first path is primary; fallback scans tagged for future removal.
  - All semantic fallback sites tagged with DM-META-REMOVE-FALLBACK markers (34 total: DomainEntityInstance 5, DomainMutationContext 5, EffectLoweringPass 7, DomainToCSharpExporter 10, OracleTool 4, MinimalApiGenerator 3).
  - Metadata-first path is always the primary (fast) path; fallbacks are guarded by null checks.
  - Fallback scans NOT yet removed — removal deferred until AnalysisResult is universally required.
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
  - Nullable AnalysisResult internal signatures preserved for dual-use paths (EntitySyntaxPass vs export pipeline)
  - Fallback scans preserved behind DM-META-REMOVE-FALLBACK markers for future removal when AnalysisResult is universally required
  - Follow-ups F1–F31 all resolved; no open items (./dacr-followups-2026-07-30.md)
