# Decision: Immutable Core for Domain Modeling (V3)

**Date:** 2026-05-31  
**Status:** Accepted  
**Deciders:** User (primary), with analysis support

## Context
After two independent cost-benefit analyses (technical/engineering downstream consequences + consumer/product long-term flexibility), the following was established:

- The current V2 implementation (`Poly/Data/Modeling`) uses internally mutable record types + a large Command/Intent transactional mutation subsystem (~65 command subtypes, ~50 JSON-polymorphic intents, `Domain.Mutation` builder, engine switch, `DomainMutationCollection` rollback helper, per-Domain lock).
- This delivers strong immediate consumer value (MCP agents get live, incremental, traceable, rollback-safe domain evolution via `ApplyWithTrace`, sessions, revision snapshots, and affordances).
- However, the mutation layer concentrates growing incidental complexity. Every new domain concept pays a high "mutation tax."
- The V3 direction (`Poly/DomainModeling`) defines true immutable records (`Domain`, `Entity`, `Stage`, `Action`, `CreateEntityInstance` with `PropertyBinding` initializers, unified `DomainExpression`, etc.) constructed via fluent builders. Models implement `Node` and reuse the existing `Syntax.Analysis` infrastructure for analyzers and metadata.

## Decision
We adopt the **immutable record core** (V3 shape in `Poly/DomainModeling`) as the target for the domain model types.

**Non-negotiable requirement:** The model evolution pattern must be maintained. Model correctness (analysis + validation + atomic application with rollback on error + rich traces) remains a hard requirement for consumers (especially LLM/MCP agents and lowering pipelines).

We will **not** abandon the transactional, diagnosable, safe-evolution experience. Instead, we will re-express it as a thinner, cleaner layer over immutable roots (pure transformations + structural diff for traces + validation gate).

## Rationale (from the two analyses)
- **Engineering sustainability:** Immutable eliminates the bulk of the bespoke mutation plumbing, private-field mutation sites, lock + rollback dance, and duplicated intent/command shapes. Downstream (analysis metadata via side table, lowering, snapshots, concurrency, persistence, reproduction) becomes simpler and more robust.
- **Consumer value:** The live editing loop (incremental + traces + rollback + sessions) is the highest-value capability delivered today. We keep it. Immutability enables better future capabilities (cheap branching, clean runtime instance semantics, expression-rich effects, simulation).
- **Alignment:** Matches project principles (domain model as primary artifact, minimal incidental complexity, builders, reuse of Syntax.Analysis).

## Consequences
- Core model types in `Poly/DomainModeling` are now the direction of record.
- `Poly/Data/Modeling` (V2) remains the current integrated surface (MCP, demos, lowering, rich analyzers) during transition.
- We must design and implement a thin "evolution applicator" (or patch/intent interpreter) that preserves `CreateMutation` / `ApplyWithTrace` / rollback-on-analysis-error / trace semantics over immutable `Domain` instances.
- NodeId continuity strategy for incremental analysis across versions is required.
- Expressiveness gaps (cross-entity effects, dynamic calculations via `DomainExpression`, ownership variants) become first-class priorities on the new foundation.
- Short-term: dual maintenance while consumer surfaces (MCP tools, demos, lowering per AGENTS.md rules) are bridged or ported.
- Long-term: lower maintenance tax, higher flexibility for powerful consumer features.

## Next Steps (Immediate)
1. Keep the immutable V3 core building cleanly (achieved 2026-05-31).
2. Design the minimal evolution layer that delivers equivalent (or better) transactional correctness guarantees on immutable roots.
3. Unify analysis contracts, diagnostic codes, and lowering rules.
4. Use the documented roadblocks (library cross-entity + dynamic values, healthcare ownership) as the forcing function for the next wave of expressiveness on the immutable + `DomainExpression` foundation.
5. Preserve and evolve the MCP/agent-facing experience (traces, sessions, safe rollback) as a first-class deliverable.

## Not in Scope (for now)
- Immediate deletion of the V2 mutable implementation.
- Full cutover of all demos/MCP/lowering before the evolution layer is proven on the immutable core.

This decision prioritizes long-term maintainability of the implementation while protecting (and ultimately improving) the capabilities that consumers — especially LLM-driven agents — rely on for correct, evolving domain models.