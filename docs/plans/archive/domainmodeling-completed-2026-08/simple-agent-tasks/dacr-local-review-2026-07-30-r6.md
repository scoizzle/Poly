# DACR r6 review — 2026-07-30 (phenomenal-review, standard)

- **Target**: local (uncommitted) — full dirty tree (DACR code + fail-closed tests + plan docs + agent protocol)
- **Mode**: standard (adversarial; primary evidence; sibling-path + reachability rules applied)
- **Issue counts**: 0 bugs, 2 suggestions, 2 nits (F32/F35 closed in this review’s docs deliverable; F33–F34 remain open)
- **Verdict**: **Ship-ready for DACR runtime/MCP code** relative to former r5 blockers — F24 (B-2) is **fixed in source** with sibling SA fallthrough + standalone regression test. Gate G2 marker total **34** matches live greps. Pick-order docs updated in-pass (F32); residual open items are G2 wording (F33) and a DescribeStage nit (F34).
- **Process notes**: r5’s sibling-path lesson paid off — this pass re-verified both SA paths from current files (not r4/r5 quotes). Marker total re-summed (5+5+7+10+4+3 = **34**). HEAD baseline for SA was re-checked via `git show HEAD:…DomainEntityInstance.cs` (legacy predicate present) and current scan path (predicate restored at lines 274–286).

## Summary

The working tree contains a mature DACR metadata-first migration: domain-keyed MTI, dual-path SA fallthrough (metadata `TryResolveAction` + analysis-absent scan), fail-closed describe routes when analysis is present, RLM throw in the exporter, ESM/RCM/SDPM throws on notify, and a substantial fail-closed test surface (including B-2 standalone). Residual `DM-META-REMOVE-FALLBACK` tags remain by design (suite Done Definition item 4). Dominant risk for the next agent is **not** B-2, but **stale pick-order docs** that still send work at F24 after the fix landed.

## Issues

### Issue 1 -- Severity: suggestion

- File: `docs/plans/simple-agent-tasks/dacr-README.md:14` (also `:30`; `dacr-followups-2026-07-30.md`)
- Description: Pick order and follow-ups status claimed **r5 open — F24 (B-2) blocking**, while `dacr-gate.md` states F1–F31 resolved and G3 documents F24 SA fallthrough on the scan path. Primary evidence: `DomainEntityInstance.cs:274-286` restores the legacy effects+policies SA predicate on the analysis-absent branch; `DomainEntityInstanceTests.InvokeAction_Standalone_EmptyStageCopyWithParams_FallsThroughToEntityAction` forces that path.
- Suggestion: Mark F24–F31 disposition closed; update README pick order.
- Status: **closed in r6 docs deliverable** (F32)

### Issue 2 -- Severity: suggestion

- File: `docs/plans/simple-agent-tasks/dacr-gate.md:21` vs residual evolution scans
- Description: G2 claims “fallbacks are guarded by null checks.” Runtime dual paths in `DomainEntityInstance` largely match (scan only when `analysis is null`; create/link fail closed when analysis present). `DomainMutationContext` still has live structural scans for in-batch mutation resolution (5 tags) that are intentional dual-use, not analysis-null-only. The gate claim over-generalizes beyond runtime invoke/transition.
- Suggestion: Narrow G2 wording to “runtime semantic routes guard scans with analysis-null (or fail closed when analysis present); evolution mutation context retains live-tree scans for in-batch resolution (tagged).”
- Status: open

### Issue 3 -- Severity: nit

- File: `Poly.Mcp/Tools/OracleTool.cs:571-573` (`DescribeStage` analysis-absent fallback)
- Description: `StageCapabilityMetadata? stageCap = null` then `stageCap?.View…` is dead null-forgiving; counts always fall back to stage lists. Harmless, but leftover from the analysis-present split.
- Suggestion: Use `stage.Actions.Count` / `stage.Policies.Count` directly on the null-analysis path.
- Status: open

### Issue 4 -- Severity: nit

- File: `docs/plans/simple-agent-tasks/dacr-followups-2026-07-30.md` (r1 disposition table)
- Description: Historical table still said “F1 still open” under strip-metadata row while F1 is `[x]` above.
- Suggestion: Fix the historical row to “Fixed (F1)”.
- Status: **closed in r6 docs deliverable** (F35)

## Verified-correct notes (primary evidence)

| Claim | Evidence |
|---|---|
| **F24 / B-2 closed in code** | Scan path SA at `DomainEntityInstance.cs:274-286`; metadata path at `DomainSemanticLookupExtensions.cs:68-72`; sibling predicates both effects+policies only (no Parameters). Regression: `InvokeAction_Standalone_EmptyStageCopyWithParams_FallsThroughToEntityAction`. |
| **F26 soft scans removed** | `CreateChildInstance` / `ExecuteCreateInRelationship` / `GetOutboundRelatedInstances` throw on TryGet* miss with analysis present. |
| **F27 dead null check** | `NotifyTransition` uses only `!TryGetValue` then `continue` with documented best-effort comment (`DomainInstanceStore.cs:158-164`). |
| **F30 ARM.StageByName gone** | `ActionResolutionMetadata` is only `(EntityActions, StageActions)` in `DomainModelMetadata.cs:30-33`. |
| **F28 marker total** | Live greps: DomainEntityInstance 5, DomainMutationContext 5, EffectLoweringPass 7, DomainToCSharpExporter 10, OracleTool 4, MinimalApiGenerator 3 → **34**. Gate G2 matches. |
| **F17 reachability** | Throw when analysis present + ESM null or stage missing; comments state unreachable on consistent analyze+instance trees. Severity remains posture-hardening, not valid-input breakage. |
| **F4 describe soft-scan** | Analysis-present paths return not-found before `DM-META-REMOVE-FALLBACK` blocks. |
| **F5 exporter RLM** | `ResolveRelationship` throws when analysis present and RLM null. |
| **F31 protocol** | Sibling-path, reachability, primary evidence, Pass B template present in `docs/agent/phenomenal-review.md`. |

## Checklist

- [x] Diff collected; scope drift noted (protocol docs + DACR + plans)
- [x] Stance: adversarial / assume wrong
- [x] Producer/consumer keys (MTI domain, ARM entity, RLM default, ESM entity, SDPM stage)
- [x] Sibling-path SA (metadata + scan) checked; both have tests forcing path
- [x] Fail-loud reachability considered for F17/F25
- [x] Marker total recomputed (34)
- [x] HEAD SA baseline re-read via `git show` (not chain-trusted from r5 alone)
- [x] Review file under `docs/`
- [x] Follow-ups section updated in `dacr-followups-2026-07-30.md`
- [ ] Full suite re-run this session (optional; gate claims 1728 green — not re-executed here)
