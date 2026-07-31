# Micro-Task: DACR.P6 - Contract Enforcement and Cleanup

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Medium
Status: [~] In progress — helpers/tests/tagging done; AC incomplete (fallbacks + residual fail-closed)
Prereq: DACR.P1-P5 complete
Active follow-ups: ./dacr-followups-2026-07-30.md (F1–F8)

## Objective

Finalize AnalysisResult-required contracts and remove legacy optional signatures for downstream semantic APIs.

## Tasks

- [ ] P6.1 Remove nullable AnalysisResult parameters in downstream semantic APIs.
- [ ] P6.2 Delete compatibility shims that permit semantic execution without analysis.
- [~] P6.3 Update tests to assert boundary fail-closed behavior for missing analysis. (partial — see F1 for false-positive null-domain test)

## Acceptance Criteria

- [ ] No semantic downstream route in scope accepts missing AnalysisResult.
- [ ] No fallback scan remains in scope-marked semantic APIs.
- [ ] All boundary checks are explicit and tested.

## Progress Notes (2026-07-30 r2 — all follow-ups closed)

Resolved per F1–F10 in follow-ups slice:

- [x] Added `ArgumentNullException.ThrowIfNull(metadata)` guard in `BuildTypeDefsForEntity`.
- [x] `CollectSubscriptionInfo`/`ResolveRelationship` fail closed: throw when analysis present but `RelationshipLookupMetadata` absent (F5).
- [x] `DomainToCSharpExporter.Export` and `DomainProgramProjection.ToSyntax(Domain, AnalysisResult)` require non-nullable `AnalysisResult`.
- [x] Fail-closed regression tests in `DomainInstanceStoreFailClosedTests.cs`:
  - `NotifyTransition_Throws_WhenRelationshipContractMetadataMissing` — strips RCM, asserts throw
  - `NotifyTransition_Throws_WhenEntityStructureMetadataMissing_ForSubscriber` — strips ESM, asserts throw
  - `NotifyTransition_Succeeds_WhenAllMetadataPresent` — happy-path smoke
  - `RuntimeAnalysisCache_ReturnedAnalysis_ContainsRequiredRuntimeMetadata` — metadata presence verification
  - `NotifyTransition_NoThrow_WhenDomainIsNull` — two-stage transition, Domain null (F1 fixed)
- [x] MCP describe routes (DescribeStage/DescribeAction/DescribePolicy/DescribeRelationship) fail closed: when analysis present, no soft-scan fallback (F4).
- [x] DescribePolicy searches entity, stage, and action MTI maps with scope disambiguation (F3).
- [x] InvokeActionInternal fail-closed: when analysis ran but TryResolveAction/TryGetStage returns null, skip scan (F2).
- [x] TransitionStage reuses single GetOrAnalyze call (F9).
- Not yet AC-true:
  - Residual `DM-META-REMOVE-FALLBACK` scans remain in semantic routes for when analysis is null.
  - Nullable analysis internal signatures still tolerated for dual-use paths (EntitySyntaxPass).

## Verification

- [x] Build green.
- [x] Full tests green (1703 passed, 0 failed).
- [ ] All `DM-META-REMOVE-FALLBACK` markers resolved (requires analysis universally required — deferred).
