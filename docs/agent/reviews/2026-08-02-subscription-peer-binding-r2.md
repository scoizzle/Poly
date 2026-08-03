# Subscription peer binding r2 — 2026-08-02

- **Target**: local uncommitted changes (peer binding + F1–F10 full-send)
- **Mode**: multi (Pass A = session; Pass B = independent read-only subagent)
- **Issue counts**: 1 bug, 5 suggestions, 1 nit — **all closed in F11–F16 fix-up 2026-08-02**
- **Verdict**: **Ship-ready** after F11–F16. Suite **1785** green.
- **Diff stats**: 18 tracked files, +709 / −209 (+ 2 review/follow-up docs untracked)

## Summary

Re-review after F1–F10 full-send. Stage-level peer binding (parse/print, dispatch `PeerBinding`, store Each/Any/All arg pass, runtime rewrite, F1 unbound root, event ban, export refuse, dead event bag removed) is real in source. Residual: entity-level `when` never runs effect-binding validation (F1/F3/F4/event sibling), missing automated oracles for export refuse / nested / assign-target, Link/Unlink Target not in bind/collect, assign target still rewritten at runtime if IR bypasses analysis. Follow-ups F1–F10 marked `[x]` must not hide these.

## Prior F1–F10 disposition (primary evidence)

| ID | Disposition | Evidence |
|----|-------------|----------|
| F1 | **fixed** stage; **entity residual** → Issue 1 | Unbound root error stage path + `SubscriptionEffect_PathPrefixWithoutPeerBinding_AnalysisError` |
| F2 | **fixed** code; **oracle incomplete** → Issue 3 | `DomainToCSharpExporter` throws on `PeerBinding` |
| F3 | **fixed** code; **oracle incomplete** → Issue 4 | `NestedPeerPath` analysis |
| F4 | **fixed** code; **oracle incomplete** → Issue 4 | `PeerAsAssignTarget` analysis |
| F5 | **fixed** | Quantifier + `DateOperation` in `BindPeerInExpression` |
| F6 | **partial** → Issue 1 | Entity peer binder errors; bare entity-level warns only; no effect binding |
| F7 | **fixed** create-in/filter; Link residual → Issue 2 | |
| F8 | **mostly fixed**; gaps → Issues 3–5 | |
| F9 | **fixed** | No `_eventValues` / `SetPeerInstance` in tree |
| F10 | **fixed** | Keys/comments include PeerBinding |

## Issues

### Issue 1 -- Severity: bug

- File: `Poly/DomainModeling/Analysis/SubscriptionAnalyzer.cs:36-52` (contrast stage `ValidateSubscription` → bindings ~294)
- Description: Entity-level `when` only handles peer binder (error) vs warn. It never runs `ValidateSubscriptionEffectBindings`. Unbound peer-like roots, nested peer, peer assign target, and legacy `event` are **not** fail-closed on entity-level. Evolution can **Accept** (warnings only) domains that violate guide fail-closed claims. C# projection still collects entity-level subs while store notify does not fire them.
- Suggestion: Call the same binding validation on entity-level (with relationship/target resolution where possible), or error **all** entity-level `when` until runtime exists; align guide.
- Status: closed — entity-level runs `ValidateSubscription`; fixture fixed `when fines Resolved`.
- Found by: Pass B (Pass A concurs)

### Issue 2 -- Severity: suggestion

- File: `SubscriptionAnalyzer.cs` Link/Unlink `break`; `DomainEntityInstance.BindPeerInEffect` `_ => effect`
- Description: `LinkRelationshipEffect` / `UnlinkRelationshipEffect` carry `Target` expressions but are not collected or peer-rewritten. Not DSL-authorable today; IR residual.
- Suggestion: Bind/collect `Target`, or analysis-reject link/unlink in subscription bodies.
- Status: closed — Target collected + peer-rewritten.
- Found by: Pass B

### Issue 3 -- Severity: suggestion

- File: `DomainToCSharpExporter.cs:368-374`
- Description: F2 export refuse implemented; no unit/oracle asserts throw on peer-dependent sub.
- Suggestion: `Export_PeerDependentSubscription_Throws` (or MCP smoke).
- Status: closed — `Export_PeerDependentSubscription_Throws`.
- Found by: Pass B

### Issue 4 -- Severity: suggestion

- File: analysis NestedPeerPath / PeerAsAssignTarget
- Description: F3/F4 implemented without dedicated tests (only F1/event/entity-level oracles exist).
- Suggestion: Stage-level analysis tests for nested peer + assign-to-peer IR.
- Status: closed — nested + assign-target analysis tests.
- Found by: Pass B

### Issue 5 -- Severity: suggestion

- File: `DomainInstanceStore.cs:196-222`
- Description: Any/All pass `PeerBinding` (code OK); no runtime test forces Any/All + peer rewrite. Product DSL parses quantifier as Each only.
- Suggestion: IR store test for Any/All + peer, or document product Each-only.
- Status: closed — `SubscriptionEffect_AnyQuantifier_PeerBinding_CopiesPeerProperty`.
- Found by: Pass B

### Issue 6 -- Severity: suggestion

- File: follow-ups + r1 review “ship-ready / all F closed”
- Description: Docs overclaimed full closure after full-send relative to Issue 1 and missing oracles.
- Suggestion: New residual tasks F11+; do not leave false complete on entity-level binding.
- Status: closed — F11–F16 tracked and done; r2 issues closed.
- Found by: Pass B + Pass A

### Issue 7 -- Severity: nit

- File: `DomainEntityInstance.cs:707-709`
- Description: Runtime still rewrites assign **Target** via `BindPeerInExpression`. Analysis rejects peer l-values; corrupt plans still get Literal targets instead of bind-time throw.
- Suggestion: Throw in `BindPeerInEffect` if assign target is under peer binder.
- Status: closed — `RejectPeerAssignTarget` throws.
- Found by: Pass B

### Issue 8 -- Severity: nit (Pass A)

- File: `SubscriptionAnalyzer.cs:328-334` error text
- Description: Unbound-root error message always says “no peer binder was declared,” even when `PeerBinding` is set but the path-prefix root matches neither the binder nor a relationship.
- Suggestion: Branch message: missing binder vs unknown root with binder `X`.
- Status: closed — branched diagnostic text.
- Found by: Pass A

## Verified-correct notes

- Stage Each + peer scalar assign end-to-end green path + tests.
- Notification-only; parse/print `as name`; plan carries PeerBinding; store passes binder on Each and Any/All.
- Stage F1 unbound root + legacy event analysis tests.
- Export refuse throw present for PeerBinding.
- Dead event bag removed; subscription flag try/finally cleanup test updated.
- Guide documents scalar peer, export limit, stage preference.

## Checklist

- [x] Diff collected (18 files + review docs)
- [x] Multi Pass B
- [x] Sibling-path (stage vs entity-level; VM vs export; Each/AnyAll args)
- [x] Reachability (export throw on peer-dependent export; F1 analysis on stage)
- [x] Primary re-verify of F1–F10 claims
- [x] Review under `docs/agent/reviews/`
- [x] Follow-ups residual under `docs/plans/simple-agent-tasks/`
