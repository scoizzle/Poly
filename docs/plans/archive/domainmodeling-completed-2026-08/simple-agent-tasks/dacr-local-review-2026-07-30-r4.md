# DACR r4 review — 2026-07-30 (full-send slice, multi-pass)

- **Target**: local (uncommitted) — r4 delta (F15–F23)
- **Mode**: multi (Pass A = reviewer context; Pass B = fresh split-context subagent, diff-only)
- **Issue counts**: 1 bug, 2 suggestions, 4 nits
- **Verdict**: NOT fully closed — F16–F18/F21–F23 are genuinely fixed and tested; **B-2 (bug)** keeps the r4 slice open: F15's fix restored SA fallthrough on the metadata path only, leaving the same silent-no-op class reachable via the analysis-absent scan path. Small fix, but it needs a harden pass (r5).
- **Process notes**: The B-1 class recurred because the SA fallthrough lives in two paths (metadata + scan) and the fix verified only one. The F15 fix message said "the runtime silently no-ops (Phase 3 §6e)" — the scan path violates exactly that stated invariant. Regression tests cover only the analysis-present path; no standalone (`Domain == null`) stage-copy test exists anywhere in the suite.

## Summary

The r4 slice resolved F15–F23: restored the SA fallthrough predicate in `TryResolveAction` (dropping the `Parameters.Count` check), guarded the lowering fallback scans, made dispatch fail loud on an unresolvable stage, added a 23-test fail-closed surface, unified `DescribePolicy` on `DomainElementData`, disambiguated the ESM throws, and refreshed gate docs. Pass B traced the producer/consumer identity chains (SDPM Stage-object keying vs `ESM.StageByName` values — same `entity.Stages` references, no mismatch), the reachability of the new throws (unreachable on legitimately-analyzed domains: immutable per-evolve domains, `ConditionalWeakTable` keyed by reference, `TransitionStage` bounded to model stages), the metadata strip keys in the new tests (match producers), and the F22 JSON shape (camelCase preserved). The dominant residual is the semantics drift between the two action-resolution paths (B-2) and the un-unified fail-closed posture across the four ESM-miss consumers (S1).

## Issues

### Issue 1 -- Severity: bug (found by Pass B)
- File: `Poly/DomainModeling/DomainEntityInstance.cs:261-271` (`InvokeActionInternal`, analysis-absent scan path)
- Description: The legacy runtime scan had the SA fallthrough — `if (action is not null && action.Effects.Count == 0 && action.Policies.Count == 0 && entityAction is not null) action = entityAction;` (r3 verified at `git show HEAD:DomainEntityInstance.cs:290`). The refactored scan branch instead does stage-first then `action ??= Entity.Actions.FirstOrDefault(...)` — `??=` never replaces a found-but-empty stage copy. So the exact B-1 chain (`AddAction` → `AddParameterToAction` → `AddActionToStage` — copy inherits params, no effects → `AddStageTransitionEffect` entity-only) silently no-ops on a **standalone instance** (`DomainEntityInstance.Create(entity, values, domain: null)`, a documented public path; `Create` sets `CurrentStage` from the first stage). The F15 comment at `DomainSemanticLookupExtensions.cs:63-70` states the invariant ("must fall through — otherwise the runtime silently no-ops (Phase 3 §6e)") — the metadata path honors it, the scan path does not. Both F15 regression tests run only the analysis-present path; no standalone stage-copy invocation test exists.
- Suggestion: Restore the legacy predicate in the scan branch after the stage lookup (before `??=`), and add a standalone regression test (entity with stage copy + entity action carrying a transition effect, `domain: null`, assert the transition fires).
- Status: open

### Issue 2 -- Severity: suggestion
- Files: `Poly/DomainModeling/DomainEntityInstance.cs:285-292` (throw), `Poly/DomainModeling/DomainEntityInstance.cs:587-620` (`TransitionStage` drops OnExit/OnEntry on miss), `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:118-150` (F16 drops exit/entry effects on miss), `Poly/DomainModeling/DomainInstanceStore.cs:158-162` (`continue` on stage-miss)
- Description: Four consumers of the same `EntityStructureMetadata.StageByName` miss with analysis present encode three different contracts: `InvokeActionInternal` throws (F17); `TransitionStage` and `EffectLoweringPass.StageTransition` silently skip effects; `NotifyTransition` silently skips the subscriber. Gate G3 sells F17 as part of a unified fail-closed contract, but three of four paths are fail-open on the same condition. Reachability today is posture-only (same `entity.Stages` source; immutable per-evolve domains; fresh analysis per domain reference; `TransitionStage` bounded to model stages), so this is not a reachable wrong behavior — but the F17 throw and its neighbors now encode contradictory contracts, and the F17 message conflates "ESM absent" with "stage not found".
- Suggestion: Either downgrade the F17 throw to a documented skip (matching neighbors) or upgrade the neighbors to throw; at minimum comment `TransitionStage` / `NotifyTransition` on why their miss degrades open while `InvokeActionInternal` throws, and distinguish ESM-absent from stage-not-found in the throw message.
- Status: open

### Issue 3 -- Severity: suggestion (found by Pass B)
- Files: `Poly/DomainModeling/DomainEntityInstance.cs:715-718, 753-756, 794-797, 811-814, 908-912` (`CreateChildInstance`, `ExecuteCreateInRelationship`, `GetOutboundRelatedInstances`)
- Description: The F16/F2/F9 pattern guards fallback scans with `&& analysis is null`. The F21-touched runtime paths still run the structural scan whenever `TryGetEntity` / `TryGetRelationship` returns false — i.e., with analysis present and the metadata stripped, they silently scan instead of failing closed. Gate G2's claim "fallbacks are guarded by null checks" is false for these sites. Unreachable in production flows (metadata always present under a full analysis), but contradicts the r4 slice's own stated pattern in the very method F21 edited.
- Suggestion: Guard these scans with `&& analysis is null` to match F16, or document the deliberate exception in the gate.
- Status: open

### Issue 4 -- Severity: nit (found by Pass B)
- File: `Poly/DomainModeling/DomainInstanceStore.cs:158-162`
- Description: `StageByName` is a `Dictionary<string, Stage>` built by `ToDictionary` — values are never null — so `|| subscriberStage is null` is dead. The F23 note claimed the dead `if (subscriberStage is null) continue;` was removed; the merged dead null-check survived. This `continue` is also the one soft path in an otherwise-throwing block (see Issue 2).
- Suggestion: Drop `|| subscriberStage is null`.
- Status: open

### Issue 5 -- Severity: nit (found by Pass B)
- File: `docs/plans/simple-agent-tasks/dacr-gate.md:20` and the F19 note in `dacr-followups-2026-07-30.md`
- Description: Per-file marker counts in the same gate evidence log (10+5+7+10+4+3) sum to **39**; both docs say "~36 total". Workspace-wide grep confirms no uncounted file contains markers.
- Suggestion: Correct to 39.
- Status: open

### Issue 6 -- Severity: nit (found by Pass B)
- Files: `docs/plans/simple-agent-tasks/dacr-gate.md` (Remaining risks bullet), `docs/plans/simple-agent-tasks/dacr-README.md:14,30`
- Description: The gate Remaining risks bullet reads "F15 — B-1 SA fallthrough regression, blocking; F16–F18 suggestions; F19–F23 nits" — as if F15 is still open/blocking — while the follow-ups doc status is `[x]` closed. `dacr-README.md` still says follow-ups "closed per r2 slice" / "per r2+r3 slices". F20 was supposed to fix this wording; both docs remain stale and the README never reflected r4.
- Suggestion: Restate the bullet as resolved (point at closure status + the genuinely open residuals in r5); update the README status table to the true current state.
- Status: open

### Issue 7 -- Severity: nit
- File: `Poly/DomainModeling/Analysis/RuntimeContractAnalyzer.cs:55,67` + `ActionResolutionMetadata` (`DomainModelMetadata.cs`)
- Description: After F23 switched `DomainInstanceStore` from `ActionResolutionMetadata.StageByName` to `EntityStructureMetadata.StageByName`, `ARM.StageByName` has no production consumer — only the producer and the fail-closed test assert it. Dead metadata surface introduced by the r4 delta.
- Suggestion: Remove `StageByName` from `ActionResolutionMetadata` (and the test assertion) or tag it with `DM-META-REMOVE-FALLBACK`-style marker / a comment.
- Status: open

## Verified-correct notes (both passes)

- **F15 predicate** (`DomainSemanticLookupExtensions.cs:63-71`): matches legacy effects+policies check; parameters correctly excluded; `AddActionToStageChange` (`DomainChange.cs:595-597`) confirmed to copy parameters; unit test `TryResolveAction_ParamCarryingStageCopy_FallsThroughToEntityAction` and the McpSmoke chain test are real and would fail under the old predicate.
- **SDPM identity chain**: `SubscriptionDispatchPlanMetadata` keyed by the Stage node (`RuntimeContractAnalyzer.cs:195`); `NotifyTransition` reads via `ESM.StageByName` — same `entity.Stages` instances in the same tree; `NodeMetadataStore` keys by `NodeId`. No mismatch.
- **F17/F23 throw reachability**: unreachable on legitimately-constructed, consistently-analyzed instances (same-stage source, immutable domains, fresh analysis per new reference, `TransitionStage` bounded to model stages).
- **F18 tests**: 23 tests; `Remove<T>` keys match producers exactly (`RelationshipLookupMetadata`/`DomainTypeLookupMetadata` under `default`, `MutationTargetIndexMetadata` under the domain node, ESM/ARM under the entity node); describe not-found tests strip from `state.LatestAnalysis`, which is what the routes read; `ResolveRelationship` throw/not-found/found distinction correct.
- **F22 JSON**: both `DescribePolicy` return sites use `DomainElementData` (six camelCase fields incl. optional `expression`); additive; consistent with the other three routes; the pre-existing policy-oracle test still passes.
- **F21**: hoisting is behavior-neutral (cache-backed). **F16**: guards match the F9 runtime pattern. **F19**: per-file counts match the tree (total is 39 — see Issue 5).

## Checklist

- [x] Diff collected; scope drift noted (r4 delta only; r1–r3 already reviewed)
- [x] Stance: adversarial; split-context applied (Pass B = fresh subagent, diff-only)
- [x] Producer/consumer keys traced for new lookups (SDPM stage-object, ESM/ARM entity, RLM/DTLM default, MTI domain)
- [x] Null / partial / not-found / missing-contract outcomes distinct (Issue 2: not fully — ESM-absent vs stage-not-found conflated in F17 message)
- [x] Same-shape-different-meaning considered (scan path vs metadata path SA — Issue 1)
- [x] Fail-closed tests actually strip dependencies (verified keys + reachability)
- [x] Oracles not weakened (no silent skip/delete/stub-to-green)
- [x] Plan/gate status matches residual work (Issues 5, 6: counts + wording stale)
- [x] Review file written under `docs/` (this file)
- [x] Follow-up tasks written under `docs/` (r5 section in `dacr-followups-2026-07-30.md`)
- [x] Prior follow-ups dispositioned (r4 items in r5 section)
- [x] User given paths + top issues
