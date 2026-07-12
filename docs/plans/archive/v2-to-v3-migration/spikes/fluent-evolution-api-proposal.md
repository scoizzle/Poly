# Proposal: A Fluent, Ergonomic Evolution API

**Date:** 2026-06  
**Status:** Exploration / Proposal

## Motivation

**Note (June 2026):** This spike was written before the decision to drop the explicit `EvolutionTransaction` / `BeginTransaction` / `Commit` model. See the resolution of Open Question #8 in `docs/decisions/2026-05-31-evolution-layer-design.md`.

The new simpler foundation makes the fluent surface even cleaner. The old low-level example no longer applies.

**Current starting point (post-decision):**

```csharp
// Batch
var result = evolution.Apply(changes);

// Fluent (the surface we are investing in)
var result = evolution
    .Evolve()
    .AddEntity("Order")
        .WithProperty(...)
        .AddAction("PlaceOrder", a => ...)
    .Apply();
```

The V3 fluent builders (one-shot construction) still exist for comparison:

```csharp
new DomainBuilder("ECommerce")
    .AddEntity("Order")
        .WithProperty(...)
        .AddAction("PlaceOrder", a => ...)
    .Build();
```

**Question:** Can we make the `Evolve()` fluent evolution path *at least* as ergonomic as the dedicated builders, so that for agent-driven work we can deprioritize or largely eliminate the separate builder API?

This document explores one possible shape.

## Proposed API Shape (High Level)

See `fluent-evolution-api-sketch.cs` for a more detailed (still spike-level) implementation sketch.

Core idea:

```csharp
// Evolving an existing domain
var result = currentDomain.Evolve()
    .AddEntity("Order")
        .WithProperty("Id", stringType)
        .WithProperty("Status", orderStatusType)
        .AddAction("PlaceOrder")
            .WithParameter("CustomerId", stringType)
            .WithEffect(e => e
                .Create("OrderItem")
                .Set("Quantity", Parameter("qty")))
    .AttachPolicy("Order", "ValidStatusTransitions")
    .Apply();

// Initial creation
var newDomain = Domain.EvolveFromScratch("ECommerce")
    .AddPrimitive("string", TypeCategory.Text)
    .AddEntity("Customer")
        .WithProperty("Email", emailType)
    .Commit();
```

### Proposed Fluent Methods (Initial Cut)

On `FluentEvolutionBuilder`:
- `AddPrimitive(string name, TypeCategory category)`
- `AddEntity(string name)` → returns `FluentEntityBuilder`
- `AttachPolicy(string targetEntity, string policyName)` (or fluent policy builder later)

On `FluentEntityBuilder`:
- `WithProperty(string name, string typeName)`
- `AddAction(string name)` → returns `FluentActionBuilder`
- `AddStage(string name)` (with optional parent)
- `And()` to go back to domain level

On `FluentActionBuilder`:
- `WithParameter(string name, string typeName)`
- `WithEffect(Action<FluentEffectBuilder> configure)`

On `FluentEffectBuilder`:
- `Create(string entityType)`
- `Set(string propertyName, object value)` (supporting `Parameter(...)` references)
- `Publish(...)`, `TransitionStage(...)` etc.

This surface can be implemented as a thin layer on top of `EvolutionTransaction.Apply(...)`. All changes are still recorded and go through the analysis gate on `Commit()`.

## Benefits

- Single primary surface for LLM/MCP agents.
- Construction and evolution use the same mental model and the same analysis/trace/rollback guarantees.
- Reduces the need to maintain two parallel "nice" APIs (builders vs evolution).
- Makes the "try changes safely" loop the happy path even during initial modeling.

## Open Questions / Trade-offs

- Performance: Does every small step need to go through full analysis, or can we have a "fast path" for construction that only does full analysis on `Commit()`?
- Initial creation ergonomics: Is `Domain.Create("Name").AddEntity(...)` nicer or worse than the current `new DomainBuilder("Name").Entity(...)`?
- How much of the existing builder investment can be reused or adapted?
- Do we still want *any* pure one-shot construction API for human test code?

## Recommendation

Treat making the Evolution API itself the primary ergonomic surface as a first-class goal for Phase 1.

The fluent evolution API should be designed to be compatible with (or eventually replace) the existing `DomainMutationIntent` surface for MCP/agent use. This gives us a single, coherent, fluent way for agents to both construct and evolve domains while still getting full analysis, traces, and rollback guarantees.

The existing fluent builders (`DomainBuilder` etc.) can be kept as a lightweight convenience for human test code and simple one-off scenarios, but they should no longer be considered a co-equal primary API for the V3 system.

## Next Sketching Steps

- Expand the spike in `fluent-evolution-api-sketch.cs` with more methods and a full realistic example based on the ECommerce demo.
- Prototype a minimal version of the fluent surface on top of the current EvolutionTransaction skeleton.
- Evaluate the experience against the current `DomainBuilder` syntax.