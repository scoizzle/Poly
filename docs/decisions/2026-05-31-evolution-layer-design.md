# Evolution Layer Design Sketch — Preserving the Model Evolution Pattern on the Immutable Core

**Goal:** The analysis-gated, correctness-preserving evolution experience that consumers (especially LLM/MCP agents) rely on today must continue to exist and improve after we adopt the immutable record core — without unnecessary transactional ceremony.

This document is the starting point for the thin "applicator" layer that replaces the heavy V2 Command/Intent machinery. The layer delivers atomicity, rich diagnostics, and traceable change history as a natural consequence of immutable values + a single analysis gate, not by emulating database transactions.

## Core Contract to Preserve (for Consumers)
- Propose a batch of changes against an immutable domain snapshot.
- The system produces a proposed new root (or reuses unchanged subtrees).
- Run analysis on the proposal (ideally incremental via stable NodeIds).
- On any **error diagnostics** → do not adopt the new root; return the original root + rich diagnostics + a high-fidelity `EvolutionTrace`.
- Always return an `EvolutionResult` containing the (new or original) root, the `AnalysisResult`, and an `EvolutionTrace` (steps, affected nodes, timing, error/warning counts, success/rollback flag).
- Support incremental analysis where possible (via stable NodeId continuity on unchanged subtrees).
- Expose via MCP (ApplyMutationWithTrace style) and C# for demos/tests.

**Key mental model shift from V2:** There is no mutable state being patched and then repaired. "Rollback" means "the caller continues to hold the original snapshot; the proposed root is simply discarded." Atomicity is free because the input domain was never mutated.

## Target Shape (Recommended after resolving Open Question #8)

```csharp
var evolution = new DomainEvolution(currentImmutableDomain);

// Simple batch form (primary for most agent use)
var result = evolution.Apply(changes, priorAnalysis);

// Fluent ergonomic form (the surface agents and future UI will primarily use)
var result = evolution
    .Evolve()
    .AddEntity("Order")
        .WithProperty("Id", stringType)
        .AddAction("PlaceOrder", a => a
            .WithParameter("CustomerId", stringType)
            .WithEffect(e => e.Create("OrderItem")))
    .AttachPolicy("Order", "ValidStatusTransitions")
    .Apply(priorAnalysis);

if (!result.Succeeded)
{
    // Agent sees exactly why (diagnostics), the prior snapshot, and rich trace for recovery/branching/explanation
    // The original domain reference the caller held is untouched.
}
else
{
    current = result.Root;   // new immutable root + analysis + trace
}
```

`DomainChange` (or a thin adapter over `DomainMutationIntent` for compatibility) is the input currency. The applicator uses V3 construction mechanisms (builders or pure record helpers) to produce the proposed new root.

The fluent `Evolve()` builder is the primary ergonomic surface for both construction-like and incremental change work. It still funnels everything through the single analysis gate and produces the same `EvolutionResult` / `EvolutionTrace` contract.

## Key Differences from V2 (the complexity we remove)
- No 65 `XxxCommand` records with `Apply`/`Rollback` pairs.
- No `DomainMutationCollection` indexed remove/restore hack.
- No per-Domain mutation lock on the model objects themselves (immutable values make concurrent mutation impossible by construction; serialization for sessions is a separate, orthogonal concern).
- No private mutable list poking.
- No compensating rollback logic at all. On analysis failure the proposed root is simply discarded; the original snapshot remains valid.
- Trace is built from the declared change list + the final analysis result (plus optional structural diff for fidelity). No side effects during mutation.

## Open Design Questions (to resolve in next execution)
1. **Change representation** — Keep the existing ~50 `DomainMutationIntent` types as the wire/MCP form (good for compatibility), or introduce a smaller, higher-level `DomainChange` set that the immutable core understands directly?
2. **Id continuity** — How do we copy stable `NodeId` values into new immutable record instances for unchanged subtrees so incremental analysis stays cheap?
3. **Deep immutable updates** — Do we invest in a small set of "with" helpers / lenses for the common nested updates (add property to entity, add effect to action on stage, etc.), or generate them, or accept builder-mediated construction for every change?
4. **Trace fidelity** — How close do we want the new traces to be to the old per-command `DomainMutationStepTrace`? (Affected nodes, ordering, etc.)
5. **MCP surface (agent interaction model)** — The MCP is the primary surface agents use. We will deliberately optimize the MCP tools, interaction patterns, batching strategy, affordances, error feedback, and intent shapes for how models actually use tools. This includes both incremental improvements on the current catalog and more fundamental redesign where it improves model effectiveness. 

   The evolution layer must make this possible by being a clean, observable, correct, and incremental engine that an agent-optimized MCP layer can drive reliably. Compatibility with existing `ApplyMutationWithTrace` / session behavior is table stakes during transition, but not the end state.
6. **Ergonomics of the Evolution API itself** — Can/should the Evolution API be made fluent and ergonomic enough (via a lightweight `Evolve()` builder) that the separate fluent builder API becomes unnecessary or significantly deprioritized? (See updated exploration below.)
7. **Real-time UI / Visualization Requirements** — The Evolution layer must support a full real-time visual authoring experience (see dedicated section).

**Question 8 (Resolved June 2026):** See the dedicated "Decision: No Full Transaction/Commit Model Required" section immediately below. The short answer is that the full `BeginTransaction` / `Commit` / explicit rollback ceremony is **not required** on an immutable core and was an unnecessary carry-over from V2's mutable compensating-transaction design.

## Decision: No Full Transaction/Commit Model Required (Resolved)

**Decision (June 2026):** The Evolution layer does **not** use an explicit transaction / commit / rollback abstraction (`EvolutionTransaction`, `BeginTransaction()`, `Commit()` with rollback semantics). 

**Rationale:**

- In V2 the heavy transaction machinery existed because the domain model was internally mutable. `Apply()` steps mutated live object graphs in place; paired `Rollback()` commands + a `Domain._mutationLock` were required to guarantee that analysis failure did not leave the domain corrupted. The "transaction" was a compensating transaction over shared mutable state.
- In V3 the domain model consists of immutable records. A batch of changes is accumulated as data (`DomainChange` list). The applicator produces a **new** proposed `Domain` value. On analysis failure the caller simply continues to hold the original snapshot; the proposed value is discarded. There is no mutable state to repair and therefore no need for compensating actions or an explicit transaction coordinator.
- The observable behavior agents and UI need (atomicity from the caller's perspective, rich diagnostics on failure, excellent traces, ability to branch from the prior root) is delivered entirely by the immutable snapshot + single analysis gate + `EvolutionResult` return shape. Adding `BeginTransaction` / `Commit` language adds ceremony without additional safety or clarity.
- "Rollback" remains a visible concept in `EvolutionResult.WasRolledBack` and in the trace for agent understanding and UI animation, but it is a **result flag**, not an operation that mutates anything back.

**What we still need and will invest in:**
- A clean `DomainEvolution.Apply(changes)` entry point.
- A lightweight fluent batch builder surfaced via `evolution.Evolve()... .Apply()` (or similar finalizer name) so that the evolution path itself can be the primary ergonomic surface for agents and future visual UI authoring.
- `EvolutionResult` and `EvolutionTrace` remain the primary output contract (with the "rolled back" case clearly represented).
- All the UI requirements (fine-grained observable change events, optimistic application, stable NodeId identity, support for high-frequency human-driven edits) are unaffected — they are orthogonal to whether we call the batch accumulator a "transaction."

**Consequences:**
- `EvolutionTransaction` and `BeginTransaction()` are removed from the design and implementation.
- The fluent evolution builder (the high-priority ergonomic investment) is layered directly on the simpler `DomainEvolution` + change applicator.
- Documentation, micro-tasks, and code must be updated to remove the database-style language.

This decision directly supports the Core Engineering Principles: remove incidental complexity that does not improve correctness, operability, or time-to-value; build the smallest coherent mechanism that delivers the required guarantees.

## Layering Decision: Evolution on Top of Builders (Not the Other Way Around)

**Decision:** The Evolution layer sits **on top of** the V3 fluent builders (or a thin pure construction abstraction), not the reverse.

**Rationale against the inverted approach ("Builders on top of Evolution"):**

- Builders provide a reasonable ergonomic construction API for humans and occasional one-shot construction. However, they are not expected to be the primary interface for LLM/MCP agent work. The evolution layer (via `DomainMutationIntent` compatibility or a native `DomainChange` surface) is likely to be the dominant interaction model for agents.
- Putting every builder operation through the full transactional machinery (change recording, analysis gate, potential rollback, trace generation) would make one-shot construction significantly heavier and more surprising.
- Construction mistakes would trigger rollback semantics and traces, which is the wrong mental model for building a new domain.
- It would couple the simple construction path to the more complex transactional + analysis system, violating "build working code before abstraction" and making the core construction experience depend on the full evolution machinery.

**Correct layering (current decision):**

- **Builders** (or a lower-level `IDomainConstructor` / pure construction helpers) = the mechanism for producing new immutable `Domain` instances from scratch or from deltas.
- **Evolution layer** = the transactional coordinator that:
  - Accepts batches of changes (via `DomainChange` or `DomainMutationIntent` adapter)
  - Uses the construction mechanism to produce a proposed new immutable root

---

## Can the Evolution API Itself Be Made Ergonomic Enough to Obviate Separate Builders?

**Updated position (June 2026):** Yes — this is now considered a first-class goal.

The fluent builder API was originally viewed as one of the major ergonomic wins of V3. Upon reflection, the Evolution layer (the transactional change application system) was always going to be a hard requirement for agent/MCP compatibility and model correctness. It is very likely the primary way agents will interact with the model going forward.

We should therefore treat "making the Evolution API itself fluent and pleasant to use for construction-like tasks" as a high-priority design objective in Phase 1, rather than a secondary concern. If successful, this can significantly reduce (or even largely eliminate) the need to invest heavily in the separate fluent builder surface.

The separate builders can be kept as a lightweight convenience for human test code and simple one-off scenarios, but they should no longer be treated as a co-equal primary API.

### Potential Shape of an Ergonomic Evolution API

The simpler core (no transaction object) makes the fluent surface even cleaner. The primary low-level form is now:

```csharp
var result = evolution.Apply(changes);
```

The high-priority ergonomic investment (see spikes) is a fluent builder:

```csharp
var result = evolution
    .Evolve()
    .AddEntity("Order")
        .WithProperty(...)
        .AddAction(...)
            .WithEffect(...)
    .Apply();
```

See `spikes/fluent-evolution-api-proposal.md` (to be refreshed post-decision) and `spikes/fluent-evolution-api-sketch.cs` for the current exploration of this fluent surface. The goal remains: make the evolution path itself the most pleasant way for agents (and later humans via UI) to perform both construction and incremental change while still getting the full analysis gate, trace, and rollback-on-error behavior.

### Pros of Making Evolution the Primary Ergonomic Surface

- Single mental model for agents: "I always talk to the system by proposing changes/evolutions."
- Avoids maintaining two parallel construction APIs (builders vs. evolution).
- The analysis + rollback + trace machinery becomes part of the happy path even for construction, which is arguably good for correctness.
- Aligns with how agents already tend to work (propose changes rather than generate perfect one-shot structures).

### Cons / Risks

- Every construction step now carries the weight of the transactional machinery (even if optimized).
- The "one-shot creation" experience might feel heavier than a pure builder.
- Risk of over-engineering the change application path for the sake of ergonomics.
- Initial creation (`CreateDomain`) might still feel different from ongoing evolution.

### Recommendation

This is worth serious exploration. The Evolution API does **not** have to remain low-level and command-oriented. We can (and probably should) invest in making a fluent, expressive change-oriented API that feels as good as or better than the current builders for agent use.

If we succeed at this, the dedicated fluent builder API can be deprioritized or reduced in scope (kept mainly for human test code and very simple one-off scenarios).

This would be a material simplification of the overall V3 surface.

**Next step suggestion:** Prototype the fluent `Evolve()` builder on the simplified (non-transactional) core and compare the experience directly against the current `DomainBuilder` syntax on one of the demo domains or the PersonLifecycle example. Evaluate whether success here allows us to treat the dedicated fluent builders as a secondary convenience for human test code.

This ergonomic question remains a first-class design driver for Phase 1. The analysis gate, rich trace, and clear rollback-in-result behavior must still be excellent even on the fluent path.

## Success Criteria for the Layer
- The evolution layer can support an agent-optimized MCP surface (both incremental improvements and more fundamental redesign of tools/interaction patterns for how models actually use them), while preserving the core model evolution guarantees (analysis gate, rich traces, correct rollback-on-error behavior, observable changes).
- During transition, existing MCP tools (`ApplyMutationWithTrace`, session revisioning, `ExplainInvalidDomain`, etc.) can be pointed at an immutable-backed implementation with no (or minimal) behavior change for callers. Long-term, the MCP surface itself is expected to evolve for better model ergonomics.
- All three demo domains + lowering pipelines continue to work during transition.
- Adding a new modeling concept (e.g. a new effect kind or ownership variant) no longer requires touching 5–10 mutation plumbing files.
- Model correctness (analysis errors → clear failure result with original root + excellent diagnostics + trace) remains as strong as today or stronger. "Atomicity" is now a natural property of immutable values rather than something enforced by compensating transaction logic.

This layer is the concrete embodiment of "I love the immutable core, and the model evolution pattern will be maintained because model correctness is a requirement."

---

## Real-time UI / Visualization Requirements (New Major Concern)

The Evolution layer is not only for LLM/MCP agents. A key long-term use case is building a **visual authoring UI** that can render changes (including those proposed by LLMs) in real time, allowing humans to see, understand, and potentially intervene in the modeling process live.

This adds several new requirements on the Evolution layer and change model:

### 1. Fine-grained, Structured Change Events (beyond coarse final traces)
- A single `EvolutionTrace` after `Apply()` (or the fluent builder's finalizer) is useful for agents but insufficient for a live UI.
- The system needs to be able to emit **observable, incremental change events** as changes are applied (or at least in a way that a UI can efficiently diff and update visuals).
- Events should be rich enough for a UI to answer questions like:
  - "Which visual node was affected?"
  - "What exactly changed on this entity (property added, stage changed, action added)?"
  - "What was the before/after state for this specific element?"

### 2. Stable Visual Identity
- `NodeId` continuity (already important for incremental analysis) becomes **critical** for UI work.
- Visual elements (nodes in a diagram, cards in a list, etc.) must be able to reliably track the same conceptual entity across many mutations without flickering or losing selection/layout state.

### 3. Optimistic Application + Reconciliation
- A UI may want to apply LLM-proposed changes *optimistically* for immediate visual feedback, then react when the full analysis completes (success → keep changes; error → show diagnostics and potentially animate rollback).
- This requires the Evolution layer to support a clear "proposed state" that can be rendered separately from the last committed state.

### 4. Low-latency Feedback Loops
- For a good live visualization experience, analysis feedback should be fast enough to feel responsive.
- Batching + incremental analysis (leveraging NodeId stability) will be essential.

### 5. Rich History / Branching / Undo
- Real-time UIs often benefit from easy access to previous versions of the domain (for undo, branching, "what if" exploration).
- Because the model is immutable, this is naturally easier than in V2, but the Evolution layer should expose convenient ways to navigate history.

### 6. Human UI-Driven Changes (New)
In addition to LLM/MCP agents proposing changes, **human users will directly manipulate the domain through UI controls** (e.g., adding entities, editing properties, wiring actions and effects, changing stages, etc.).

This means:
- The Evolution layer must support high-frequency, granular, human-driven changes coming from interactive UI elements.
- These changes still need to go through the same analysis gate, traceability, and rollback-on-error behavior.
- The UI will often want to apply changes **optimistically** (produce and render a proposed root immediately) for immediate feedback, then reconcile when the full analysis completes (success → adopt; error → show diagnostics and animate the fact that the proposal was rejected).
- The change model (`DomainChange`) and observation mechanisms must be rich enough to support both:
  - Coarse LLM-proposed batches, and
  - Fine-grained, interactive human edits.
- A fluent, control-friendly evolution API becomes even more important (the UI will likely drive changes through something like the fluent evolution surface we're exploring, not raw low-level `DomainChange` objects).

### Implications for Change Representation & API Design

- `DomainChange` (or the native change model we settle on) may need to be more structured and observable than a pure internal implementation detail.
- We may want an **observable stream** of fine-grained changes (or a way to subscribe to them) in addition to the batch `EvolutionTrace`.
- The fluent evolution API we're sketching should consider visual/UI ergonomics **and** direct human manipulation, not just LLM generation ergonomics.

These UI requirements (real-time visualization + direct human editing) significantly strengthen the argument for investing early in a clean, first-class native `DomainChange` model and rich observation capabilities, rather than only providing an adapter over the old `DomainMutationIntent` surface.

### Anti-Patterns to Avoid in Phase 1 Implementation

To prevent major architectural problems when we later build the visual authoring UI, the following should be actively avoided while implementing the Evolution layer and change model:

- Making `DomainChange` (or the primary change representation) too opaque or stringly-typed, with no rich, inspectable structure that a UI can reason about.
- Providing only coarse final traces (after `Apply()`) with no mechanism for fine-grained, observable, incremental change events during or after a batch.
- Weak or non-durable `NodeId` stability across mutations (this will break visual identity, selections, layout, and drag-and-drop in a UI).
- Designing the change application path as strictly batch-only with no support for optimistic application or incremental observation.
- Over-coupling the change application logic to the old V2 `DomainMutationIntent` model without a clean, evolvable native `DomainChange` abstraction.
- Assuming all changes will come from "agent-style" batches; the model and APIs should feel natural for granular, interactive human edits coming from UI controls.
- Hiding too much of the mutation machinery behind the fluent builders in a way that makes direct, programmatic, or UI-driven changes second-class or awkward.

Keeping these anti-patterns top of mind during early implementation will save substantial rework later.

---

Next concrete work (post this decision):
- Implement the simplified `DomainEvolution` with `Apply(changes)` + `Evolve()` fluent builder entry points.
- First native `DomainChange` subtypes + applicator logic (using V3 builders internally).
- Prove end-to-end: successful evolution + analysis error → rolled-back result with rich trace on a PersonLifecycle slice.
- Ensure the evolution layer supports both near-term compatibility adapters for existing MCP tools and the longer-term goal of an actively optimized MCP agent interaction surface.
- Prototype the fluent `Evolve()` surface in parallel and compare ergonomics against the dedicated builders.

**New high-priority consideration for the above work**: Design the change model and observation points with real-time UI visualization + direct human editing in mind from the beginning. Avoid the anti-patterns listed above. The removal of the transaction wrapper makes the observation model and optimistic-update paths simpler to reason about.