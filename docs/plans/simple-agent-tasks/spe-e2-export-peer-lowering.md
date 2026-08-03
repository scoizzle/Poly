# SPE-E2 — Export peer path-prefix lowering

**Stream:** E  
**Difficulty:** M  
**Status:** `[x]`  
**Soft prereq:** E1  

## Objective

Lower subscription effect bodies so binder path-prefix (`order Code`) becomes member access on the peer **parameter**, not `this`.

## Required reading

- E1 result / handler signature  
- `EffectLoweringPass` / `DomainExpressionLoweringPass` / `LoweringContext` (action parameter names + UseThisReference)  
- VM `BindPeerInExpression` (behavior oracle — do not copy bag rewrite into export)  

## Exact steps

1. When building handler body for peer-dependent sub, configure lowering so:
   - `this` / bare props = subscriber entity  
   - path-prefix root == `PeerBinding` → `Parameter(PeerBinding)` as subject for inner property  
2. Prefer reusing `LoweringContext` / expression pass knobs over a one-off rewrite.  
3. Nested peer path-prefix remains unsupported (analysis already rejects) — fail loud if present.  
4. Ensure notification-only handlers unchanged.

## Verification

- [x] Build green  
- [x] Structural/Syntax assertion: assign target `this.Status`, value `order.Code` (or equivalent Member chain) for a peer-dependent sample  
- [x] Existing export tests still green  

## File ownership

- **Edit:** `DomainToCSharpExporter.cs`, possibly `LoweringContext` / `DomainExpressionLoweringPass` / `EffectLoweringPass` if shared knobs needed  
- If touching shared lowering, keep API minimal and document for VM non-regression  
- **Do not edit:** store notify, entity-level analysis  

## Progress notes

### 2026-08-02 — implement + verify (pass, severity nit)

**Implement success:** true · **Verify pass:** true · **Severity:** nit  
Static read of current tree (no git CLI/shell in verifier session; no build/test re-run). Build/suite green not re-proven; structural AC evidenced in source.

- **Exporter (~374–406):** peer-dependent subscription handler sets `LoweringContext.Parameters[PeerBinding] = Parameter(PeerBinding)` when `PeerBinding` length > 0 and `UseThisReference` true.
- **Expression pass (~87–101):** `DomainExpressionLoweringPass.RelationshipNavigation` — Parameters hit → `Route(TargetProperty, parameterSubject)`; else `Member(_currentSubject, name)`; nested under binder throws `InvalidOperationException`.
- **Docs (~16–21):** `LoweringContext.Parameters` remarks document peer-binder export use and VM bag rewrite non-regression.
- **Tests (~447–502):** `Export_PeerDependentSubscription_LowersPeerPathPrefixToParameterMember` asserts assign Destination `Member(ThisReference, "Status")` and Value `Member(Parameter("order"), "Code")`.
- **Notification-only (~504–548):** peer params only when `PeerBinding` set; `Export_NotificationOnlySubscription_HandlerRemainsParameterless` still asserts zero params + zero-arg notify.
- **Sibling path:** VM `BindPeerInExpression` rewrites binder `RelationshipNavigation` to peer-bag `Literal` before lower (`DomainEntityInstance.cs` ~689–760); export keeps live `Parameter` member — dual mechanisms, both correct for scalar peer field assign. Nested: `SubscriptionAnalyzer` NestedPeerPath + existing `SubscriptionEffect_NestedPeerPath_AnalysisError`.

## Status

**Status:** Done  

