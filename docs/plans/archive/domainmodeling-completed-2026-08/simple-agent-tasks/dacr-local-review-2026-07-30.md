# DACR local review — 2026-07-30 (r1)

- **Mode**: uncommitted local changes (staged + unstaged + untracked)
- **Branch**: `rewrite/domainmodeling-from-scratch`
- **Scope**: DACR helpers, runtime/MCP/lowering metadata-first routing, fail-closed tests, plan status, local scratch
- **Diff stats**: 17 tracked files (+390/−148); 9 untracked
- **Issue counts**: 6 bugs, 4 suggestions, 3 nits
- **Verdict**: Not ship-ready as “DACR complete.”
- **Superseded for work tracking:** open follow-ups live in [`dacr-followups-2026-07-30.md`](./dacr-followups-2026-07-30.md) (r2). Do not treat this r1 list as the active task queue — many items below are fixed; use the follow-ups file disposition table.

## Summary

This change set advances DACR by introducing `DomainSemanticLookupExtensions`, routing runtime/MCP/lowering lookups through metadata-first helpers, and marking P0–P6 + gate complete. The direction is sound (shared lookup surface, SA fall-through in `TryResolveAction`, tagged residual scans), but several paths are not actually metadata-backed yet: `MutationTargetIndexMetadata` is published on the **domain** node while consumers look it up with `default`, so `GetEffectivePolicies` and the new `DescribePolicy` metadata branch never hit analysis. Fail-closed tests are largely happy-path renames, and `NotifyTransition` softens a prior throw into silent skip. Unrelated scratch/WIP (SQLite DBs, `MidstreamOrderEntry.cs`, RestApi route edits, `dumb` in the poly demo) should not ship with this work. Dominant risks: wrong MCP stage policy counts, premature “complete” status while fallbacks and nullable analysis remain, and accidental commit of local demo artifacts.

## Issues

### Issue 1 -- Severity: bug

- File: `Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs:89`
- Description: `GetEffectivePolicies` calls `analysis.GetMetadata<MutationTargetIndexMetadata>(default)`, but `RuntimeContractAnalyzer` publishes the index with `context.SetMetadata(domain, …)` (domain-keyed). `NodeMetadataStore.Get` only falls back from a concrete node to `NodeId.Empty`, never the reverse, so MTI is always null here and the method always returns an empty list.
- Suggestion: Look up with the domain node (e.g. require `Domain` or resolve via existing domain-keyed helper), or publish MTI on `default` consistently with `RelationshipContractMetadata` / `RelationshipLookupMetadata`. Add a unit test that asserts non-empty effective policies for an entity with entity/stage policies after full analysis.
- Status: open

### Issue 2 -- Severity: bug

- File: `Poly.Mcp/Tools/OracleTool.cs:643`
- Description: `DescribePolicy` uses the same wrong key (`GetMetadata<MutationTargetIndexMetadata>(default)`). With analysis present, the metadata-first branch always sees `mti is null` and falls through to the direct domain scan. That means the “migrated” path never exercises metadata and still only finds entity-level policies (stage/action policies remain invisible, same as the structural scan).
- Suggestion: Fix the MTI key as in Issue 1, then resolve policies from `EntityPoliciesByEntity`, `StagePoliciesByEntity`, and `ActionPoliciesByEntity` (not only entity-level). Add an MCP/oracle test for a stage- or action-scoped policy name.
- Status: open

### Issue 3 -- Severity: bug

- File: `Poly.Mcp/Tools/OracleTool.cs:544`
- Description: When `LatestAnalysis` is present and a stage is found, `DescribeStage` reports `stagePolicies.Count` from broken `GetEffectivePolicies`, so effective policy counts regress to **0** even when the entity/stage has policies. The previous path used `StageCapabilityMetadata.View.EffectivePolicies`, which was analysis-correct.
- Suggestion: Prefer `StageCapabilityMetadata` (or fix `GetEffectivePolicies` and stop aggregating all action-level policies). Match the fallback path’s use of effective policy counts so analysis-present and analysis-absent outputs stay consistent for the same domain.
- Status: open

### Issue 4 -- Severity: bug

- File: `Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs:103`
- Description: Even after fixing the MTI key, `GetEffectivePolicies` adds **every** entity-action policy for the entity when describing a stage. That is not stage-effective policy semantics (contrast `EffectivePoliciesMetadata` / `StageCapabilityMetadata`, which compose entity + stage policies for that stage). DescribeStage would over-count guards that only apply to unrelated actions.
- Suggestion: For stage-scoped effective policies, use entity + that stage’s policies only (and optionally stage-local action policies if product semantics require it). Do not fold all `ActionPoliciesByEntity` entries into a stage describe count.
- Status: open

### Issue 5 -- Severity: bug

- File: `Poly/DomainModeling/DomainInstanceStore.cs:150`
- Description: Previously missing `ActionResolutionMetadata` threw (`Runtime dispatch requires ActionResolutionMetadata…`). The new path uses `TryGetStage` and **`continue`s** when metadata/stage resolution fails. Missing `EntityStructureMetadata` (or empty `StageByName`) now silently skips all subscribers instead of failing closed, so subscription side effects can no-op without error.
- Suggestion: Restore fail-closed: if analysis ran but required stage/action resolution metadata is absent for a live subscriber, throw with an explicit message. Only `continue` when the stage name is known-absent on a fully populated map.
- Status: open

### Issue 6 -- Severity: bug

- File: `Poly.Tests/DomainModeling/Analysis/DomainInstanceStoreFailClosedTests.cs:43`
- Description: Tests named for fail-closed / missing-metadata behavior do not remove or withhold metadata. `NotifyTransition_Throws_WhenRelationshipContractMetadataMissing` only asserts a fresh analysis *has* `RelationshipContractMetadata`. `NotifyTransition_RequiresActionResolutionMetadata_ForSubscriber` only asserts happy-path `ThrowsNothing`. Gate/P4/P6 claim “fail-closed regression tests” are unearned.
- Suggestion: Inject or construct an `AnalysisResult` missing the contract (or a test double for `RuntimeAnalysisCache`) and assert `InvalidOperationException` with the expected message. Keep the happy-path coverage separately with accurate names.
- Status: open

### Issue 7 -- Severity: suggestion

- File: `docs/plans/simple-agent-tasks/dacr-gate.md:689`
- Description: Gate G2 claims “No semantic fallback scan remains in touched routes,” and P0–P6 + gate are marked `[x] Complete`, while production still retains many `DM-META-REMOVE-FALLBACK` scans (explicitly documented as residual) and P6 acceptance criteria / tasks remain unchecked for removing nullable analysis and fallbacks. Status overstates shipped contract tightness.
- Suggestion: Keep phases `[~]` or split “helpers + tagging” from “fallbacks removed.” Align G2 wording with “fallbacks tagged / primary path metadata-first” until removal is done. Do not mark P6 complete while AC items for no nullable analysis / no fallbacks are still open.
- Status: open

### Issue 8 -- Severity: suggestion

- File: `Poly/DomainModeling/DomainEntityInstance.cs:44`
- Description: `TryResolveAction` treats a stage action with no effects, policies, **or parameters** as an empty SA copy and falls through. Legacy SA only checked effects and policies (parameters alone still replaced with entity action when present). Empty stage-only actions (no entity twin) return `false` from the helper and rely on the fallback scan; removing fallbacks later will drop those actions entirely.
- Suggestion: Document intentional SA change or match prior predicate. When a stage action exists but is empty and no entity action exists, return the stage action (`true`) so metadata-only routes stay complete without the scan fallback.
- Status: open

### Issue 9 -- Severity: suggestion

- File: `demo/Poly.RestApi/Program.cs:36`
- Description: Unrelated WIP: route `/api/books/{isbn}` now binds unused `id`, runs a filtered query into `books` that is discarded, then still `FindAsync(isbn)`. `Book.Id` was added in `_all.cs`. Looks like local generator/dogfood scratch, not DACR.
- Suggestion: Revert RestApi + SQLite DB changes from this worktree unless they are intentional product changes; fix the route if kept (use query result, fix route template for `id`).
- Status: open

### Issue 10 -- Severity: suggestion

- File: `MidstreamOrderEntry.cs:1`
- Description: Large untracked generated C# at repo root (`MidstreamOrderEntry.cs`), plus `test/Program.cs`, `test/LibraryDbContext.cs`, `test/demo.http`, and `demo/Poly.RestApi/library.db*` (db/shm/wal). These match the “local scratch / accidental commit-in-waiting” pattern called out for this review.
- Suggestion: Leave untracked or gitignore; do not include in the DACR commit. Delete or move under a personal ignore if still needed for demos.
- Status: open

### Issue 11 -- Severity: nit

- File: `docs/experiments/examples/library-checkout.poly:149`
- Description: Diff adds `dumb: Text required` on `Loan` and a trailing-space-only change on `CheckedOutAt`. Looks accidental, not domain work.
- Suggestion: Revert the `dumb` property and whitespace-only noise.
- Status: open

### Issue 12 -- Severity: nit

- File: `Poly/DomainModeling/DomainInstanceStore.cs:170`
- Description: Dead guard `if (subscriberStage is null) continue;` remains after the earlier null check already returns/continues. Harmless but confuses readers of the fail-closed path.
- Suggestion: Remove the redundant check in a small hygiene pass.
- Status: open

### Issue 13 -- Severity: nit

- File: `Poly.Mcp/Tools/OracleTool.cs:541`
- Description: Effective action count uses `stageActions.Count + arm.EntityActions.Count` when the stage key exists, but if the stage key is missing falls back to raw `stage.Actions.Count` only (no entity actions). Counts can under-report “effective” actions relative to `StageCapabilityMetadata` and the fallback describe path.
- Suggestion: Prefer `StageCapabilityMetadata.View.EffectiveActions` when present for describe output, same as the structural fallback branch.
- Status: open
