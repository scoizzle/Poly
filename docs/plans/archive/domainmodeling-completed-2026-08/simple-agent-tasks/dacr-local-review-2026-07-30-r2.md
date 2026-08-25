# DACR r2 review — 2026-07-30

- **Mode**: uncommitted local changes (staged + unstaged + untracked)
- **Branch**: `rewrite/domainmodeling-from-scratch`
- **Scope**: r2 follow-up slice (F1–F10) on DACR helpers, runtime/MCP/lowering metadata-first routing, fail-closed tests, plan status
- **Diff stats**: 16 tracked files (+472/−159); 7 untracked (4 docs, 2 source, 2 skill wrappers)
- **Issue counts**: 0 bugs, 2 suggestions, 2 nits
- **Verdict**: Ship-ready for r2. All r1 bugs resolved. Residuals are documented soft-edges and minor hygiene.

## Summary

This r2 slice closes the 10 follow-up items (F1–F10) from the r1 review. Key fixes: fail-closed DescribePolicy via domain-keyed MTI maps with scope disambiguation (F3), MCP describe routes returning not-found instead of structural fallback when analysis is present (F4), exporter `ResolveRelationship` throwing on absent `RelationshipLookupMetadata` (F5), `InvokeActionInternal` skipping the fallback scan when analysis ran (F2), `TransitionStage` using a single `GetOrAnalyze` call (F9), and a genuine false-positive notify test replaced with a two-stage null-domain path (F1). The pre-existing test failure (`AnonymousType_PropertyAccess_Works`) is now passing: 1703/0. Plan/docs status is honest about residual `DM-META-REMOVE-FALLBACK` markers and deferred removal.

## Issues

### Issue 1 — Severity: suggestion
- File: `Poly.Mcp/Tools/OracleTool.cs:597`
- Description: `DescribeAction` metadata-first path does not use `TryResolveAction`, so it searches entity-level actions first, then stage-level — matching the structural fallback path's priority. But `TryResolveAction` (used by `InvokeActionInternal`) searches stage-first with SA empty-copy fallback to entity. When an action exists at both entity and stage level with different definitions, `DescribeAction` shows the entity version while runtime executes the stage version. This is a **pre-existing design asymmetry** (both structural and metadata paths share entity-first priority), not an r2 regression, but worth documenting.
- Suggestion: Either align `DescribeAction` with `TryResolveAction` priority (stage-first + SA fallback) or add a caller note explaining why the describe/search tool uses entity-first priority.
- Status: open

### Issue 2 — Severity: suggestion
- File: `docs/plans/simple-agent-tasks/dacr-gate.md:56`
- Description: G5 evidence says "1702 passed, 1 pre-existing failure" but the pre-existing failure (`AnonymousType_PropertyAccess_Works`) is now resolved in the r2 tree (1703/0). The gate evidence needs updating.
- Suggestion: Update G5 test count to 1703/0.
- Status: open

### Issue 3 — Severity: nit
- File: `Poly.Tests/DomainModeling/Analysis/DomainInstanceStoreFailClosedTests.cs:160`
- Description: Test name `NotifyTransition_FailClosed_WhenStoreHasNoAnalysis` asserts `ThrowsNothing` — the name suggests a fail-closed assertion but the test validates the null-domain early-return path. The behavior is correct (standalone instances without a Domain transition silently, which is the intended design), but the name is misleading.
- Suggestion: Rename to `NotifyTransition_NoThrow_WhenDomainIsNull`.
- Status: open

### Issue 4 — Severity: nit
- File: `Poly.Mcp/Tools/OracleTool.cs:570`
- Description: In the structural fallback path of `DescribeStage`, `state.LatestAnalysis?.GetMetadata<StageCapabilityMetadata>(stage)` uses a null-conditional operator, but this path is only reached when `state.LatestAnalysis is null` (the metadata-first guard returned). The `?` is dead code — harmless but misleading.
- Suggestion: Replace `state.LatestAnalysis?.GetMetadata...` with just `(StageCapabilityMetadata?)null` or remove the `?` to clarify the guard invariant.
- Status: open

## Disposition of r1 findings

| r1 Issue | r1 Severity | r2 Status | Evidence |
|---|---|---|---|
| 1. MTI default-keyed | bug | **Fixed** | `GetEffectivePolicies` passes `domain` explicitly; `DescribePolicy` passes `state.Domain` |
| 2. DescribePolicy MTI key | bug | **Fixed** | `state.LatestAnalysis.GetMetadata<MutationTargetIndexMetadata>(state.Domain)` |
| 3. DescribeStage 0 policies | bug | **Fixed** | Prefers `StageCapabilityMetadata.View.EffectivePolicies`; `GetEffectivePolicies` is entity+stage only |
| 4. GetEffectivePolicies over-aggregates | bug | **Fixed** | Comment confirms "action-level policies are not stage-effective" |
| 5. NotifyTransition silent skip | bug | **Fixed** | Throws on missing `EntityStructureMetadata`/`RelationshipContractMetadata`/`SubscriptionDispatchPlanMetadata` |
| 6. Tests don't strip metadata | bug | **Fixed** | `GetMetadataStore().Remove<T>(key)` + `Throws<InvalidOperationException>` assertions |
| 7. Plan overstates completion | suggestion | **Fixed** | Phases use [~] correctly; G2 says "fallback scans tagged for future removal" |
| 8. SA semantics drift | suggestion | **Fixed** | `Parameters.Count` check added; comment clarifies SA semantics |
| 9. RestApi WIP | suggestion | **Fixed** | Not in dirty tree |
| 10. Scratch files | suggestion | **Fixed** | Not in dirty tree |
| 11. `dumb` property | nit | **Fixed** | Not in diff |
| 12. Dead guard `subscriberStage is null` | nit | **Fixed** | Removed |
| 13. Effective action count | nit | **Fixed** | Uses `StageCapabilityMetadata.View.EffectiveActions` |
