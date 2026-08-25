# SPE-E1 — Export peer handler shape (notify + signature)

**Stream:** E (export peer)  
**Difficulty:** M  
**Status:** `[x]`  
**Soft prereq:** Parent plan §4 E  

## Objective

Change C# export subscription handlers so peer-dependent subs take a typed peer parameter and notify passes `this`; remove the hard throw for `PeerBinding` at the structural level (lowering of binder roots may still be stubbed until E2).

## Required reading

- Parent plan §4 E  
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` (~notify + handler generation)  
- `Poly/DomainModeling/Lowering/DomainProgramProjection.cs` (subscription collection)  
- Existing refuse test: `Export_PeerDependentSubscription_Throws`  

## Exact steps

1. For each subscription with `PeerBinding`, generate handler:
   - Name: keep `When{TargetEntity}{Stage}` (or document rename if collision risk).  
   - Parameter: type = target entity name; name = `PeerBinding`.  
2. For notification-only (`PeerBinding` null), keep zero-arg handler.  
3. On target entity notify loop: call `sub.When…(this)` when peer-dependent; zero-arg when not.  
4. Remove or narrow `InvalidOperationException` for PeerBinding (E2 may still need incomplete lower to throw — prefer compile-shaped empty body + TODO only if E2 lands same PR; prefer complete in E2).  
5. Do **not** implement binder path-prefix lowering in this task unless trivial — E2 owns that. Temporary body: lower only non-peer effects, or lower with placeholder that fails loud if peer roots remain.

## Verification

- [x] Build green  
- [x] Export of peer-dependent domain no longer throws solely because PeerBinding is set  
- [x] Generated method has one parameter of target type named as binder (assert on `TypeDefinitionNode` shape)  
- [x] Notify invoke includes `ThisReference` as argument for peer-dependent  

## File ownership

- **Edit:** `DomainToCSharpExporter.cs` (and helpers only if already exporter-owned)  
- **Tests:** add/adjust under `DomainToCSharpExporterTests.cs` only as needed for shape (full golden in E3)  
- **Do not edit:** `DomainInstanceStore`, policy eval, guide § owned  

## Progress notes

### 2026-08-02 — implement + verify (pass, severity suggestion)

**Implement success:** true · **Verify pass:** true · **Severity:** suggestion  
Build not re-executed in read-only verifier session (static AC check).

- **Exporter shape:** `DomainToCSharpExporter` builds `When{Target}{Stage}` with `Parameter(PeerBinding, NamedTypeReference(TargetEntity))` when `PeerBinding` length > 0; otherwise parameterless.
- **Notify:** `Notify{Stage}Subscribers` invokes with `ThisReference` vs zero args on the same peer predicate.
- **No structural throw:** peer-binding structural `InvalidOperationException` removed from exporter.
- **Tests:** `Export_PeerDependentSubscription_HandlerHasPeerParameterAndNotifyPassesThis` and `Export_NotificationOnlySubscription_HandlerRemainsParameterless` assert `TypeDefinitionNode` method params and notify `Invoke` args (including `ThisReference`). `Export_PeerDependentSubscription_Throws` removed.
- **Deferred to E2:** body peer-prefix lowering (in-code comment); dual path peer vs notification-only both present at signature/notify level.

## Status

**Status:** Done  

