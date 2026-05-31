# Virtual Actor Lowering Pass — Design Plan

## Rationale
A virtual actor model (Orleans, Proto.Actor, Akka) maps domain entities to grains, eliminating most type-definition boilerplate and cross-entity call routing from the lowering layer. This document outlines an alternative `DomainImplementationLoweringPass` that produces grain interface and implementation code instead of `TypeDefinitionNode` ASTs.

## Domain → Actor Mapping

| Domain Concept | Actor Concept |
|---|---|
| `Entity` | `IGrainInterface` + `Grain<TState>` |
| `Property` | Property on `TState` record + grain interface read methods |
| `Action` | Method on grain interface |
| `Stage` | `CurrentStage` enum field in grain state |
| `Policy` | Guard clause in method impl body |
| `Effect` | State mutation + `GrainFactory` calls in method body |
| `Event` | Not a type — becomes a grain method call |
| `EventSubscription` | Lowered away: publisher calls subscriber directly |
| `Relationship` | Navigation property on state (grain id refs) |
| `Actor` (security) | Grain-level auth attribute + identity checker |

## Generated Artifacts

For an entity `SupportCase { Title, Stage, Close() { ... } }`:

**Grain Interface:**
```csharp
public interface ISupportCaseGrain : IGrainWithStringKey {
    Task<string> GetTitle();
    Task<SupportCaseStage> GetCurrentStage();
    Task Close();
}
```

**State Record:**
```csharp
[GenerateSerializer]
public record SupportCaseState {
    public string Title { get; init; }
    public SupportCaseStage CurrentStage { get; init; }
}
```

**Grain Implementation:**
```csharp
public class SupportCaseGrain : Grain<SupportCaseState>, ISupportCaseGrain {
    public Task<string> GetTitle() => Task.FromResult(State.Title);
    public Task<SupportCaseStage> GetCurrentStage() => Task.FromResult(State.CurrentStage);

    public async Task Close() {
        // Policy guard
        if (State.CurrentStage != SupportCaseStage.InProgress) {
            throw new InvalidOperationException("Only in-progress cases can be closed.");
        }
        // Effects
        State = State with { CurrentStage = SupportCaseStage.Resolved };
        await WriteStateAsync();
    }
}
```

## Guard Accumulation Pattern

All constraint and policy guards are evaluated eagerly — no short-circuit AND over the guard
expression tree. Each guard is checked independently and a boolean accumulator (`_v`) tracks
whether any guard has failed. Effects execute only when all guards pass. The method itself
returns `true` on success and `false` when any guard fails, so callers can distinguish
completion from rejection without exception handling.

```csharp
public async Task<bool> Close() {
    bool result = true;
    if (!(State.CurrentStage == SupportCaseStage.InProgress)) result = false;
    // ... remaining guards evaluated regardless of previous results
    if (result) {
        State = State with { CurrentStage = SupportCaseStage.Resolved };
        await WriteStateAsync();
    }
    return result;
}
```

## Effect Lowering (Actor Model)

| Effect | Lowered To |
|---|---|
| `Assign(target, value)` | `State = State with { Target = value }` |
| `PublishEvent(event)` | `await GrainFactory.GetGrain<ISubscriberGrain>(id).Handler(args)` |
| `InvokeAction(action)` | `await GrainFactory.GetGrain<ITargetGrain>(id).ActionName(args)` |
| `CreateEntityInstance(type)` | `var id = Guid.NewGuid().ToString(); GrainFactory.GetGrain<ITypeGrain>(id)` |
| `DeleteEntityInstance(type)` | `await grainRef.DeactivateAsync()` (or state clear) |
| `StageTransition(stage)` | `State = State with { CurrentStage = stage }` |
| `LinkRelationship(rel, target)` | `State = State with { RelId = targetId }` |
| `UnlinkRelationship(rel, target)` | `State = State with { RelId = null }` |

## Intra-Domain Event Lowering (Actor Model)

Currently resolved at codegen time by scanning `EventSubscription` records:

```csharp
// PublishEvent(CaseAssigned) on SupportCase
// subscriptions = eventSubscriptions where EventType == CaseAssigned
// for each sub:
await GrainFactory.GetGrain<ISupportCaseGrain>(otherId).HandleCaseAssigned(args);
// OR via streams/buses
```

Subscriptions are not runtime artifacts — they're consumed at codegen.

## Comparison to Current AST Approach

| Aspect | Current (AST TypeDef) | Actor Model |
|---|---|---|
| Type structure | Manual `TypeDefinitionNode` with all members | Grain interface + state record grain impl |
| Cross-entity calls | Generated `Invoke` nodes resolved at analysis | Direct grain interface calls |
| State management | Generated property get/set in `Block` nodes | Orleans `Grain<TState>` with `ReadStateAsync`/`WriteStateAsync` |
| Event routing | Subscription lookup table or generated dispatch | Codegen resolves subscription → direct call |
| Actor/identity | Manual property definitions | `IGrainWithStringKey` / `IGrainWithGuidKey` |
| Policies | Accumulated boolean guards (all evaluated, no short-circuit) | Same — both accumulate |
| Complexity | Higher (must produce complete type ASTs) | Lower (produce interface + impl in target language) |

## Next Steps to Build

1. Create `Poly/Data/Modeling/CodeGeneration/VirtualActor/` directory
2. `ActorGrainInterfaceLoweringPass` — produces grain interfaces from entity model
3. `ActorGrainStateLoweringPass` — produces state records from effective properties
4. `ActorGrainImplementationLoweringPass` — produces grain impl with effects as method bodies
5. `ActorEffectLowerer` — reuses existing `DomainLoweringGenerator.LowerEffect` but outputs grain method calls
6. Integration with `DomainImplementationLoweringPass` as an alternative lowering target

For now, the current implementation will continue producing `TypeDefinitionNode` — the actor model pass is a separate code path that generates text/syntax output in the target actor framework's idiom.
