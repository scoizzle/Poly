# PR 51 — pipeline transformation final pass — 2026-09-04

- **Target**: PR 51 (`cursor/pipeline-transformation-1a9d` vs `master`)
- **Mode**: multi (independent Pass B + implementer pre-ship gate)
- **Issue counts**: 0 bugs, 2 suggestions (both closed in this pass), 0 nits
- **Verdict**: ship — named-action invoke looks up `session.Lower`; create/unique defaults fail closed; live execution-model guide no longer restates the deleted preprocess / always-`LowerActionBody` path

## Summary

P1–P6 plus create-defaults-on-probe and the leftover P2 lookup. Pass B found no product-path correctness bug. Two suggestions: the lookup tests did not prove `ExecuteEffectList` ran the cached node, and `docs/interpretation/domain-execution-model.md` still described preprocess-to-literals and `Stay.Create` as the action-body print. Both closed here. `rg LowerStageTransitions` and `rg PreprocessRuntimeKeyword` in `*.cs` are empty.

## Issues

### Issue 1 -- Severity: suggestion
- File: `Poly.Tests/DomainModeling/Compile/PipelineTransformationTests.cs`
- Description: Identity-after-invoke and two successful creates still pass if invoke re-lowers Effect IR and never stores. Factory `GetOrLowerOperation` would also keep the same cache identity.
- Suggestion: Replace the cached tree with a no-op `DomainResult.Success` and assert Issue creates no child.
- Status: fixed — `InvokeAction_RunsTheCachedTree_NotAReloweredEffectWalk`

### Issue 2 -- Severity: suggestion
- File: `docs/interpretation/domain-execution-model.md`
- Description: Live guide still said `ExecuteEffectList` always calls `LowerActionBody`, clocks preprocess to literals, and C# action bodies print `Stay.Create` / `CreateNav`.
- Suggestion: State lookup, pair-shaped Store jobs, factory host bind, and BCL clocks.
- Status: fixed

## What stayed residual (not filed)

EvaluatePolicy re-lowers the guard. Subscriptions / transition batches still `LowerActionBody` at execute time. Unbound `RuntimeAnalysisCache` fallback is core-catalog. `EvaluateDefaultValue` remains create-time bag fill. C# factories may wrap `Stay.Create` / `CreateNav`. Unique indexes remain EF schema. PIPELINE-STATUS CURRENT stays `(none)`.
