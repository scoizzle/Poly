# Slice 0 — Event/Relationship/Runtime Inventory

**Date:** 2026-07-17  
**Source plan:** [`dsl-sync-toward-phase1.md`](dsl-sync-toward-phase1.md)  
**Purpose:** Bill of materials for IR surgery — every event type, analyzer, DomainChange, test, builder, and MCP surface that Slice A must touch.

---

## 1. IR Records — Event-related types

| Type | File | Lines | Role | Depends on |
|------|------|-------|------|------------|
| `Event` | `Poly/DomainModeling/Event.cs` | 7 | DomainType (authorable) | `DomainType` base |
| `EventSubscription` | `Poly/DomainModeling/EventSubscription.cs` | ~15 | Subscription binding | `EventCorrelationBinding`, `EventSubscriptionRoutingMode` |
| `EventCorrelationBinding` | `Poly/DomainModeling/EventCorrelationBinding.cs` | ~10 | Key-pair binding | — |
| `EventSubscriptionRoutingMode` | `Poly/DomainModeling/EventSubscriptionRoutingMode.cs` | ~5 | Enum (Broadcast, Correlated) | — |
| `PublishEventEffect` | `Poly/DomainModeling/Effects/PublishEventEffect.cs` | ~15 | Effect that publishes event | `PropertyBinding` |

**Action:** Remove all 5 from product authoring surface. `StageSubscription` (+ quantifier enum) replaces `EventSubscription` + routing + correlation. `StageTransitionEffect` is the canonical observable (already exists). `PublishEventEffect` → delete.

---

## 2. Entity properties

| Property | Type | File | Used by (files) |
|----------|------|------|-----------------|
| `Entity.Events` | `IReadOnlyList<DomainTypeReference>` | `Entity.cs:6` | `SemanticDomainAnalyzer`, `StructuralDomainAnalyzer`, `DomainChange.cs` (4 change types), builder tests |
| `Entity.EventSubscriptions` | `IReadOnlyList<EventSubscription>` | `Entity.cs:20` | `CausalityAnalyzer`, `CorrelationAnalyzer`, `EventContractAnalyzer`, `EventFlowAnalyzer`, `ReplaySafetyAnalyzer`, `DomainChange.cs` (6 change types), `EventAnalysisTests.cs` |

**Action:** Remove both. Add `Stage.Subscriptions` (`IReadOnlyList<StageSubscription>`) in their place.

---

## 3. Effects surface

| Effect | Lowering status (EffectLoweringPass) | Direct execution (CallAction) |
|--------|--------------------------------------|-------------------------------|
| `AssignEffect` | ✅ VM — lowered via `LowerAssign()` | — |
| `CompositeEffect` | ✅ VM — lowered via `LowerComposite()` | — |
| `ConditionalEffect` | ✅ VM — lowered via `LowerConditional()` | — |
| `StageTransitionEffect` | ❌ Returns null (no lowering) | ✅ Direct — `TransitionStage()` |
| `CreateEntityInstance` | ❌ Returns null | ✅ Direct — `CreateChildInstance()` |
| `InvokeActionEffect` | ❌ Returns null | ✅ Direct — `CallAction(invoke.ActionName)` |
| `DeleteEntityInstance` | ❌ Returns null | ✅ Direct — `IsDeleted = true` |
| **`PublishEventEffect`** | ❌ Returns null | ✅ Direct — `_publishedEvents.Add(publish)` |

**Key insight:** `EffectLoweringPass.TryLowerVmNode()` handles exactly 3 of 8 effect types (assign, composite, conditional). The rest return null and fall through to `CallAction`'s direct-execution switch. `PublishEventEffect` just appends to a list — it's the most trivial direct-execution effect.

**Action:** Remove `PublishEventEffect` case from `CallAction.ExecuteEffect`. No lowering replacement needed — stage transitions are the publish.

---

## 4. Analyzers

**20 total analyzers** in `Poly/DomainModeling/Analysis/`. 8 touch event surface:

### Remove (2)

| Analyzer | Line count | Dependencies | Reason |
|----------|------------|-------------|--------|
| **`EventFlowAnalyzer`** | ~110 lines | `Domain`, `Event`, `PublishEventEffect` | Entirely about event liveness — all diagnostics invalidated by stage-observation model |
| **`CorrelationAnalyzer`** | ~80 lines | `EventSubscription`, `EventSubscriptionRoutingMode`, `EventCorrelationBinding` | Correlation is implicit via relationship path — no bindings to validate |

### Adapt (3)

| Analyzer | Current role | Adaptation |
|----------|-------------|------------|
| **`EventContractAnalyzer`** | Validates event handler param contracts | → `SubscriptionContractAnalyzer`: validate that subscription relationship path resolves, target stages exist, effect bodies bind valid `this`/`event` refs |
| **`CausalityAnalyzer`** | Detects cycles in event→subscription→action chains | Rewire from event-subscription graph to action→stage-transition→subscription graph |
| **`ReplaySafetyAnalyzer`** | Detects non-idempotent event handlers | Rewire from `EventSubscriptions` to `StageSubscription` effects |

### Keep (3 — touch event surface but not event analyzers)

| Analyzer | Event touch | Action |
|----------|------------|--------|
| **`EffectAnalyzer`** | `case PublishEventEffect:` in a switch | Remove the case; analyzer itself stays |
| **`SemanticDomainAnalyzer`** | Reads `entity.Events` | Remove `Events` iteration; analyzer stays |
| **`StructuralDomainAnalyzer`** | Duplicate event name check in `AnalyzeEvent()` | Remove `AnalyzeEvent` method; analyzer stays |

### Not affected (12)

`StructuralDomainAnalyzer` (mostly), `PolicyConstraintAnalyzer`, `ConstraintQualityAnalyzer`, `ConstraintPropagationAnalyzer`, `EnumConstraintSubsetAnalyzer`, `EffectOrderingAnalyzer`, `CapabilityAnalyzer`, `ActionParameterUsageAnalyzer`, `ContractIntegrationAnalyzer`, `RuleCoverageAnalyzer`, `AuthoringSuggestionGenerator`, `SemanticCoherenceAnalyzer`, `IdempotencySafetyAnalyzer`.

---

## 5. Diagnostic codes — 6 event-related out of 19

| Code | Constant | Currently produced by | Action |
|------|----------|----------------------|--------|
| `DMEV001` | `ActionEventContract` | `EventContractAnalyzer` | Retire; new code for subscription contract |
| `DMEV002` | `EventFlowLiveness` | `EventFlowAnalyzer` | **Retire** — stage transitions always "published" |
| `DMEV003` | `ActionOrderingCausality` | `CausalityAnalyzer` | Retire; new code for subscription causality |
| `DMEV004` | `EventCorrelationSoundness` | `CorrelationAnalyzer` | **Retire** — correlation is relationship path |
| `DMEV005` | `ActionIdempotencyReplay` | `ReplaySafetyAnalyzer` | Retire; new code for subscription replay |
| `DMEV006` | `RuleCoverage` | `RuleCoverageAnalyzer` | Keep (entity-agnostic — no change needed) |

**Rule: never renumber.** Issue new `DMSS*` (DomainModel Stage Subscription) prefix or extend `DMSEM*` range.

---

## 6. DomainChange types — 66 total

### Event-related (12) — remove from evolution surface

| DomainChange | Line | Used by tests? | Used by MCP? |
|-------------|------|---------------|-------------|
| `AddEventChange` | 496 | ✅ | ❌ |
| `RemoveEventChange` | 509 | ❌ | ❌ |
| `AddEventReferenceToEntityChange` | 519 | ✅ | ❌ |
| `RemoveEventReferenceFromEntityChange` | 532 | ❌ | ❌ |
| `AddPropertyToEventChange` | 734 | ❌ | ❌ |
| `RemovePropertyFromEventChange` | 747 | ❌ | ❌ |
| `AddEventSubscriptionChange` | 815 | ✅ | ❌ |
| `RemoveEventSubscriptionChange` | 831 | ❌ | ❌ |
| `AddEventSubscriptionCorrelationChange` | 851 | ❌ | ❌ |
| `RemoveEventSubscriptionCorrelationChange` | 874 | ❌ | ❌ |
| `SetEventSubscriptionRoutingModeChange` | 901 | ❌ | ❌ |
| `SetEventSubscriptionEventParameterChange` | 924 | ❌ | ❌ |

**EvolutionBuilder methods that need removal:** `AddEvent()`, `RemoveEvent()`, `AddEventToEntity()`, `RemoveEventFromEntity()`, `AddEventReferenceToEntity()`, `AddPublishEventEffect()`, `AddEventSubscription()`, `RemoveEventSubscription()`, `AddPropertyToEvent()`, `RemovePropertyFromEvent()`.

**To add:** `AddStageSubscriptionChange`, `RemoveStageSubscriptionChange`.

### Relationship-related (6) — keep as-is

| DomainChange | Line |
|-------------|------|
| `AddRelationshipChange` | 366 |
| `RemoveRelationshipChange` | 385 |
| `AddPropertyToRelationshipChange` | 632 |
| `RemovePropertyFromRelationshipChange` | 647 |
| `SetRelationshipShapeChange` | 776 |
| `AddStageToRelationshipChange` / `RemoveStageFromRelationshipChange` / `AddPolicyToRelationshipChange` / etc. | 947+ |

---

## 7. Builder surface

### To remove

| Builder file | Method to remove | Called from |
|-------------|-----------------|------------|
| `DomainBuilder.cs` | `Event(name)` | Examples |
| `EntityBuilder.cs` | `Event(eventName)` | Examples |
| `PublishEventBuilder.cs` | **Whole file** | `StageBuilder`, `OnEntryBuilder`, `ActionBuilder` |
| `OnEntryBuilder.cs` | `Publish(eventName, configure)` | Examples |
| `StageBuilder.cs` | `OnEntryPublish(eventName, configure)` | Examples |
| `ActionBuilder.cs` | `Publish(eventName, configure)` | Examples |

### To keep

| Builder | Note |
|---------|------|
| `DomainBuilder.cs` (rest) | Kept — only `Event()` removed |
| `EntityBuilder.cs` (rest) | Kept — `OwnsOne()`, `HasMany()`, `HasOne()`, `Event()` removed |
| `StageBuilder.cs` (rest) | Kept — only `OnEntryPublish()` removed |
| `ValueBuilder.cs` | Not affected |
| `RelBuilder.cs` | Check if exists — not found in survey |

---

## 8. Example files

| File | Event usage | Action |
|------|------------|--------|
| `PersonLifecycleExample.cs` | Creates `Event("Born")`, `Event("Died")`, uses `PublishEventEffect` on entry/exit | Rewrite to transition + stage subscription |
| `PersonLifecycleViaBuilders.cs` | `.Event("Born").Event("Died")`, `.OwnsOne(…)` | Remove `.Event()` calls; keep ownership |
| `ECommerceDomain.cs` | No events | No change |
| `LibraryDomain.cs` | No events | No change |

---

## 9. Test files — migration count

| Test file | Event refs | Action |
|-----------|-----------|--------|
| `EventAnalysisTests.cs` | 3 tests (ActionEventContract, EventFlowLiveness×2, EventCorrelationSoundness) | **Rewrite** — 3 tests become stage-subscription contract tests |
| `StructuralAnalysisTests.cs` | 1 test (duplicate event names) | **Remove** the test case (no more event names to duplicate) |
| `DomainEntityInstanceTests.cs` | 1 test (PublishEventEffect_RecordsEvent) | **Remove** the test |
| `DomainEvolutionApplicatorTests.cs` | ~50 refs (PersonLifecycle + LibraryDomain tests use events heavily) | **Rewrite** PersonLifecycle tests to use stage subscriptions; keep LibraryDomain tests (no events) |
| `EvolutionRollbackTests.cs` | 1 ref (AddEventToEntity in rollback path) | **Remove** the event line or replace with stage-subscription equivalent |

**Estimated test impact:** ~5–8 tests to remove, ~6–12 tests to rewrite. Overall suite may shrink by ~5 tests.

---

## 10. MCP surface

| File | Event exposure | Action |
|------|---------------|--------|
| `DomainTools.cs:152` | `eventCount` in `DomainOverviewData` | Remove `eventCount` field (or always return 0) |
| `DomainTools.cs` | No event-specific tools | No change |
| `README.md` | Tool table | Update if session/overview schema changes |

**MCP is clean.** No event-specific tools exist. Only the overview payload carries `eventCount`.

---

## 11. Relationship authoring paths

| Path | API | Cardinality | Ownership |
|------|-----|-------------|-----------|
| Evolution | `AddRelationshipChange(Name, Source, Target, Cardinality, Properties, SourceOwnsTarget)` | `RelationshipCardinality` enum | `SourceOwnsTarget` bool |
| Builder | `EntityBuilder.HasMany(name, targetType)` | Many | ❌ (no ownership) |
| Builder | `EntityBuilder.HasOne(name, targetType)` | One | ❌ (no ownership) |
| Builder | `EntityBuilder.OwnsOne(name, ofType)` | One | ✅ (value type only) |
| DSL (future) | `property: many owned Target` | Inline | Inline `owned` keyword |

**Normalization needed (N1):** Property-line authoring `orders: many owned Order` must construct both a `Property` (for the entity record) and a `Relationship` (on `Domain.Relationships`). The reverse side is synthesized for analysis only — no second `Relationship` record.

---

## 12. CallAction vs EffectLoweringPass ownership

**Primary host:** `DomainEntityInstance.CallAction` (line 180 in `DomainEntityInstance.cs`)

Execution flow (current):
```
CallAction:
  1. Find action by name
  2. Evaluate action policies (PolicyEvaluator / VM)
  3. Evaluate stage policies
  4. Evaluate entity policies
  5. For each effect:
     - TryLowerVmNode(effect) → if lowered, compile+execute via VM
     - else: switch on effect type (StageTransition, CreateInstance, Invoke, Delete, PublishEvent)
```

**EffectLoweringPass** (line 36, ~80 lines):
- Implements `TryLowerVmNode()` for 3 types: `AssignEffect`, `CompositeEffect`, `ConditionalEffect`
- All others → null (caller falls through to direct execution)

**Gap for Slice B:** Stage-scoped `when` subscriptions are not implemented at all — no subscription evaluation, no fan-out, no quantifier support. This is the primary runtime work.

---

## 13. Summary counts

| Category | Count | Action |
|----------|-------|--------|
| Event IR types | 5 | Remove all |
| Entity event properties | 2 | Remove both |
| Event-related DomainChanges | 12 | Remove all |
| Event-related analyzers (remove) | 2 | Delete files |
| Event-related analyzers (adapt) | 3 | Rewire to stage-subscription |
| Touching analyzers (keep) | 3 | Remove event case |
| Unaffected analyzers | 12 | No change |
| Event diagnostic codes | 5 retire, 1 move | Retire; new codes |
| Builder files with event methods | 5 | Remove methods |
| Example files with events | 2 | Rewrite |
| Test files with events | 5 | ~5–8 remove, ~6–12 rewrite |
| MCP exposure | 1 field | Remove `eventCount` |
| Relationship normalization | N1 needed | Property line → Relationship |
| CallAction direct effects | 5 | Remove PublishEvent case |
| VM effects (lowered) | 3 | No change |

---

## 14. Next: Slice A execution order

Per the plan, these tasks are ready to be broken into `simple-agent-tasks/` picks:

1. **A.1.1–A.1.3:** Add `StageSubscription` + quantifier enum + `Stage.Subscriptions` property
2. **A.2.1–A.2.6:** Remove event authoring surface (types, changes, builders, entity props)
3. **A.3.1–A.3.3:** Analyzer changes (remove 2, adapt 3, trim 3)
4. **A.4.1–A.4.3:** Relationship normalization (N1 preferred)
5. **A.5:** ADR + CORE update
6. **At.1–At.3:** Test migration
