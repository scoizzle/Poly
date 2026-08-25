# Subscription peer binding (`when Rel Stage as name`) — 2026-08-01

- **Target**: local uncommitted changes
- **Mode**: multi (Pass A = session; Pass B = independent read-only subagent, current tree)
- **Issue counts**: 2 bugs, 6 suggestions, 2 nits (all closed in full-send)
- **Verdict**: Full-send closed F1–F10 **as implemented**; **r2 re-review** found residuals — see [`2026-08-02-subscription-peer-binding-r2.md`](./2026-08-02-subscription-peer-binding-r2.md).
- **Diff stats**: 16 files, +420 / −157

## Summary

Adds optional peer binder on `StageSubscription` (`PeerBinding`), DSL parse/print (`as name`), dispatch plan plumbing, and runtime rewrite of binder path-prefix roots to literals evaluated against the transitioned peer bag. Removes the broken `event.*` bag-injection model. End-to-end **stage + Each + scalar peer field → assign** is green. Dominant residual: **analysis does not enforce** the guide’s “fail-closed if body uses binder without `as`,” so unbound path-prefix can mis-bind to the subscriber; **C# export** lowers subscription effects without peer context (sibling path). Nested peer path-prefix, assign-to-peer, entity-level `when`, and full expression rewrite coverage are incomplete relative to guide wording or dual surfaces.

## Issues

### Issue 1 -- Severity: bug

- File: `Poly/DomainModeling/Analysis/SubscriptionAnalyzer.cs:285-326`; claim `Poly.Mcp/Docs/poly-dsl-guide.md:425-426`
- Description: Guide states analysis is **fail-closed** when the body references the binder without `as name`. Implementation only validates peer props when `PeerBinding` is already set. Without a binder, path-prefix `order Code` is not classified as unbound peer access; inner names may be checked as **subscriber** props. Runtime skips `BindPeerInEffect` and lowers `RelationshipNavigation` against the subscriber bag — wrong subject, not a loud contract failure.
- Suggestion: Error when a path-prefix root is neither a subscriber relationship nor the declared peer binder (or, thinner: error when root is not a known relationship and `PeerBinding` is null). Test: withhold `as`, assert diagnostic; do not allow silent mis-bind.
- Status: closed — unbound path-prefix without binder → SubscriptionEffectBinding error; guide + tests.
- Found by: Pass A + Pass B

### Issue 2 -- Severity: bug

- File: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:363-397`
- Description: Sibling path. VM path rewrites peer via `BindPeerInEffect` + `entry.PeerBinding`. C# export builds subscription handlers with `UseThisReference` and **no peer argument**; binder path-prefix becomes `this.{binder}.{prop}` (subscriber shape), not the transitioned instance. Peer-dependent `when … as name` is incorrect on the codegen surface while present on IR/DSL/VM.
- Suggestion: Generate handlers that accept the peer (or binder locals from peer snapshot) and lower binder roots against it; or **refuse export** of peer-dependent subscriptions until that works, with a failing export/oracle test.
- Status: closed — export throws if `PeerBinding` set; guide notes VM/runtime or notification-only for export.
- Found by: Pass B (Pass A concurs)

### Issue 3 -- Severity: suggestion

- File: `Poly/DomainModeling/DomainEntityInstance.cs:758-810`; claim `Poly.Mcp/Docs/poly-dsl-guide.md:414`
- Description: Guide allows “nested path-prefix on the peer.” `EvaluateExprOnPeer` only lowers against the peer property bag (no store link resolution / `PreprocessQuantifiers`). Nested `order Item Price` does not match subscriber path-prefix semantics. Scalar peer fields are what tests prove.
- Suggestion: Narrow the guide to scalar (and owned-on-peer if later) fields, and analysis-reject relationship roots under the binder; or implement peer-as-source store-aware nav.
- Status: closed — guide scalar-only; analysis rejects nested peer path-prefix.
- Found by: Pass B

### Issue 4 -- Severity: suggestion

- File: `Poly/DomainModeling/DomainEntityInstance.cs:726-729` (Assign target rewrite); `SubscriptionAnalyzer.cs` assign walk
- Description: `assign order Code to "x"` with `as order` is not rejected as an illegal peer l-value. Runtime rewrites the assign **target** to a `Literal`, which is nonsense for assign lowering (late failure or wrong lower).
- Suggestion: Analyze-error if assign target (or other l-values) is under the peer binder; allow peer path-prefix only on values / conditions / initializer RHS.
- Status: closed — peer as assign target → analysis error.
- Found by: Pass B

### Issue 5 -- Severity: suggestion

- File: `Poly/DomainModeling/DomainEntityInstance.cs:758-810`
- Description: Incomplete peer rewrite vs expression surface: quantifier nodes (`AnyExpr`/`AllExpr`/`NoneExpr`/`CountExpr`) and `DateOperation` fall through unchanged. Peer path-prefix inside those nodes stays as binder-named `RelationshipNavigation` and is treated as subscriber relationship nav at lower time.
- Suggestion: Recurse into those node kinds (or fail closed in analysis if they appear under subscription peer roots). Add a conditional/`if` test only if product DSL can embed them in `when` bodies today.
- Status: closed — BindPeer covers quantifiers + DateOperation.
- Found by: Pass B

### Issue 6 -- Severity: suggestion

- File: `Poly/DomainModeling/Parsing/PolyDslParser.cs:763-786`; `RuntimeContractAnalyzer.cs:47-87`; `DomainInstanceStore.cs:170-198`
- Description: Entity-level `when … [as name]` parses/prints and projection may collect entity subscriptions, but VM notify only consumes **stage** `SubscriptionDispatchPlanMetadata`. `SubscriptionAnalyzer` walks stage subs only. Peer binder on entity-level is a false product surface (pre-existing entity-level runtime gap, amplified by new binder).
- Suggestion: Reject entity-level `when` at analyze until runtime exists, or wire entity-level plans into notify + analyzer.
- Status: closed — warn entity-level (codegen still uses); **error** peer binder on entity-level.
- Found by: Pass B

### Issue 7 -- Severity: suggestion

- File: `Poly/DomainModeling/Analysis/SubscriptionAnalyzer.cs:CollectPropertyAccesses` vs `BindPeerInEffect`
- Description: Analysis effect walk thinner than runtime bind: create-in initializers and invoke `Filter` are rewritten at runtime but not fully collected for peer/legacy-event diagnostics.
- Suggestion: Align collection with `BindPeerInEffect` effect kinds.
- Status: closed — create-in + invoke filter collected.
- Found by: Pass B

### Issue 8 -- Severity: suggestion

- File: `Poly.Tests/DomainModeling/DomainEntityInstanceTests.cs` (peer + notification tests); missing analysis oracles
- Description: Happy Each+peer and notification-only covered. Missing: fail-closed without `as` (Issue 1); dispatch entry carries `PeerBinding`; Any/All + peer; analysis rejects `event`; `ExecuteSubscriptionEffects_Exception_DoesNotLeakEventKeys` still oracles retired `event.*` bag keys while injection is gone (`_eventValues` write-only).
- Suggestion: Replace leak test with `_isExecutingSubscription` / no leaked peer state; add Issue 1 oracle; assert plan `PeerBinding`.
- Status: closed — F1/event/plan/entity-level/cleanup oracles landed.
- Found by: Pass A + Pass B

### Issue 9 -- Severity: nit

- File: `Poly/DomainModeling/DomainEntityInstance.cs:45,56-68`
- Description: `SetPeerInstance` snapshots into `_eventValues`, which is never read after bag-injection removal. Dead residual from the event model.
- Suggestion: Delete `_eventValues` / unused snapshot, or make peer binding the only consumer with a real read.
- Status: closed — dead snapshot removed.
- Found by: Pass B

### Issue 10 -- Severity: nit

- File: `Poly/DomainModeling/Analysis/DomainModelDiagnosticCodes.cs:26-27`; `DomainChange.cs:1121-1122` remove key string
- Description: Diagnostic comment still says `event.*`; remove `SubscriptionKey` omits PeerBinding while `SemanticMatch` includes it (analyzer key includes binder).
- Suggestion: Align comments and remove key text with peer binder.
- Status: closed — comment + SubscriptionKey include PeerBinding.
- Found by: Pass B

## Verified-correct notes

- Stage-level **Each** + peer scalar assign: `SubscriptionEffect_PeerBinding_CopiesPeerProperty` (Status → `"ABC-123"`).
- Notification-only without binder: `SubscriptionEffect_NotificationOnly_DoesNotRequirePeerBinding`.
- `RuntimeContractAnalyzer` copies `PeerBinding` into `SubscriptionDispatchPlanEntry`; store passes it on **Each and Any/All** branches.
- Parse/print round-trip: `Parse_When_WithPeerBinding_RoundTrips`.
- Semantic match for remove/duplicate includes `PeerBinding`.
- Legacy `event` root → analysis error when present.
- `_isExecutingSubscription` still try/finally-isolated for cascade notify suppression.
- Scalar peer rewrite model (`RelationshipNavigation` binder root → `EvaluateExprOnPeer` → `Literal`) is appropriate for the proven happy path.

## Checklist

- [x] Diff collected; scope = peer binding vertical (16 files)
- [x] Adversarial / multi Pass B
- [x] Sibling-path considered (VM vs C# export; Each vs Any/All arg pass; stage vs entity-level)
- [x] Reachability on new throws considered (legacy event error is reachable; fail-closed without `as` is **claimed but not implemented**)
- [x] Primary evidence from current tree + Pass B
- [x] Review under `docs/agent/reviews/`
- [x] Follow-ups under `docs/plans/simple-agent-tasks/`
