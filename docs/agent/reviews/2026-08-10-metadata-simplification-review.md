# Metadata simplification review (Transport retirement + effective-surface consolidation) — 2026-08-10

- **Target**: local — uncommitted `git diff HEAD` (21 files, +67/−309); re-verify of prior [`2026-08-10-relationship-refactor-review.md`](2026-08-10-relationship-refactor-review.md) against committed HEAD
- **Mode**: standard (re-verify of prior open items + fresh adversarial pass on the new diff)
- **Issue counts**: 0 bugs, 2 suggestions, 1 nit
- **Verdict**: **ship** — the simplification is a correct dead-weight removal; all four material prior follow-ups are fixed in committed HEAD; one low-severity behavior change and one process gap remain (filed)

## Summary

The new diff executes `domainmodeling-metadata-simplification-2026-08-10.md` honestly: deletes dead/pure-copy metadata (`RelationshipCapabilityMetadata`, `EffectiveMemberMetadata`, `EffectivePoliciesMetadata`, `TransportPass/Model/Metadata`), consolidates the effective-surface composition onto `CapabilityAnalyzer`, and publishes `OwnerEntityMetadata` once in `SemanticDomainAnalyzer`. Every deletion is verified consumer-free; removed tests were Transport-machinery-only (no oracle weakened); suite 1969 (own run, plan claim accurate). Separately, the prior review's R1–R4 are confirmed **fixed in committed HEAD** with primary evidence (compile + source reads).

## Part A — Re-verify of prior follow-ups (relationship refactor review)

| # | Item | Disposition | Evidence |
|---|------|-------------|----------|
| R1 | `create in Rel` CS1501 export arity | ✅ **fixed** | Repro domain compiles 0 errors; `CheckOut` now calls `CreateOrders(book, 0L, null)` (3 args = 3-param method) |
| R2 | guide §0.3 auto-wire overclaim | ✅ **fixed** | §0.3 rewritten: "The C# export does not yet auto-populate the child's back-reference property (e.g. `borrower` stays a constructor parameter passed as `null`)... Derived back-reference materialization is planned" |
| R3 | `Redistribute` replaced navs | ✅ **fixed** | now `e with { Navigations = [.. e.Navigations, .. rels] }` (append) |
| R4 | multi-source first-match error | ✅ **fixed** | `ResolveSourceRelationshipOrThrow` lists all: `"Declared on: {string.Join(", ", ...)}"` |
| R5 | `ModifiedNodes` per-relationship granularity | still open (nit) | `ReplaceInEntity` records entity only; harmless over-invalidation |
| R6 | E-guard (in-suite compile smoke) | **still open** | no `dotnet build`/Roslyn compile test in `Poly.Tests` |

## Part B — New diff: metadata simplification

### Issue 1 -- Severity: suggestion (RuleCoverageAnalyzer skips on unresolved transition targets)
- File: `Poly/DomainModeling/Analysis/RuleCoverageAnalyzer.cs:37-40`
- Description: previously `hasStageTransition = FlattenEffects(action.Effects).Any(StageTransitionEffect)` — any action with a transition was analyzed. Now `capability.View.TransitionTargets.Count == 0` skips. `TransitionTargets` is empty when the target stage fails to resolve in `CapabilityAnalyzer.ResolveOwnerStages` — i.e. a transition to a **nonexistent stage**. Reachability: only invalid models hit this (the missing-stage error is reported at `EffectAnalyzer:764`), so no valid-model oracle is lost — but a behavior change on erroneous input, silently. The effect-walk fallback was a genuine (if trivial) check on the bad path.
- Suggestion: when `TransitionTargets` is empty but the action has a `StageTransitionEffect`, fall back to the effect-walk check (or drop the skip comment to state "invalid targets are diagnosed in EffectAnalyzer"). Prefer keeping the canonical surface as primary with an explicit note.
- Status: open

### Issue 2 -- Severity: suggestion (MCP `hasTransport` response-field removal is a surface change)
- File: `Poly.Mcp/Tools/DomainTools.cs:105-108, 308-311, 370-373`
- Description: removing the `hasTransport` boolean from the MCP `get_domain_analysis` summary changes the tool's JSON response shape. It was a null-check with no product consumer (per the plan's census), so removal is right — but any external MCP client reading `hasTransport` will now get an undefined field. Deliberate per plan §4, and `McpSmokeTests` updated — flagging only as a "confirm external-contract tolerance" note (the MCP surface is a product contract; T2 trust bar).
- Suggestion: note the response-shape change in the MCP changelog/README if one exists; otherwise accept as intentional.
- Status: open (informational)

### Issue 3 -- Severity: nit (BehaviorPass parity is suite-verified, not contract-documented)
- File: `Poly/DomainModeling/Analysis/BehaviorPass.cs:55-62`
- Description: BehaviorPass's effective-policy source moved from `EffectivePoliciesMetadata` to `ActionCapabilityMetadata.View.EffectivePolicies`. The composition semantics (entity + stage + action) are now documented in CORE.md (DAS W2) and the plan; parity is verified only by the green suite, not a dedicated equivalence test. No defect found — the CORE.md update is the contract.
- Suggestion: optionally add a test that asserts `BehaviorPass` action policies == `ComposeStagePolicies(entity, stage) + action.Policies` for a stage action, so the consolidated composition can't silently drift.
- Status: open (optional)

## Verified-clean (adversarial checks came back empty)

- **Transport deletion complete**: no `TransportPass/Model/Surface/Metadata` references remain in prod/src/tests (remaining "Transport" hits are unrelated: `WithStdioServerTransport`, `TransportParam` naming).
- **Deleted metadata refs**: `EffectivePoliciesMetadata`/`EffectiveMemberMetadata` have zero remaining readers (grep clean).
- **OwnerEntityMetadata single-publisher**: `SemanticDomainAnalyzer.PublishOwnerIndex` (entity visit) precedes `CapabilityAnalyzer` (dependency order correct); `RuntimeContractAnalyzer.FindOwnerEntity` + `CapabilityAnalyzer` single-arg overloads consume it — no linear owner scans remain.
- **Removed tests are Transport-only**: `Analyze_ProducesTransportMetadata`, `TestInfra.Transport`, `IsExposable`/`ParentName` assertions, `Topology_ScannedOnce_SameInstanceOnModelAndTransport` — all tested the deleted machinery. The "topology scanned once" invariant still holds via `EffectTopologyPass` (only the Transport-view coupling was removed).
- **`DomainQueries.GetEntity`** reads `entity.*` directly — correct (EffectiveMemberMetadata was a documented pure copy; no inheritance in DSL).
- **Plan honesty**: `domainmodeling-metadata-simplification-2026-08-10.md` claims 1969 green; own run confirms. Deferred items (#5 ResolvedTypeReferenceMetadata, U3b) are marked not-done.

## Process notes

- The prior review's R1 (CS1501) was fixed in the committed relationship refactor — good. But R6 (the E-guard compile-smoke) remains open after **four** occurrences of the same create-arity bug class (CS7036 ×2, CS1501 ×2) without a compile oracle in-suite. This is the recurring-process finding: it should be the next admitted task.
- This simplification follows the established pattern (promote/retire facts by consumer census) and is a good model for future dead-weight passes: every deletion cites the consumer census in the plan.

## Follow-ups (checkable)

- [ ] **M1** — RuleCoverageAnalyzer: fall back to effect-walk (or explicitly document the skip) when `TransitionTargets` is empty but the action has a `StageTransitionEffect` (invalid-target models).
- [ ] **M2** — note the MCP `hasTransport` response-field removal as a surface change (changelog/README if present).
- [ ] **M3 (optional)** — equivalence test: BehaviorPass stage-action policies == `ComposeStagePolicies(entity, stage) + action.Policies`.
- [ ] **R5 (carried)** — `ReplaceInEntity` per-nav `ModifiedNodes` granularity (nit).
- [ ] **R6 (carried, process)** — the E-guard: in-suite render + compile smoke for the export; recurring create-arity bug class (4×) with no compile oracle.
