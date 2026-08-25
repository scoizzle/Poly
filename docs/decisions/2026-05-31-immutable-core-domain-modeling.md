# ADR: Adopt Immutable Core for Domain Modeling (V2 → V3 Migration)

**Date:** 2026-05-31  
**Status:** Accepted  
**Deciders:** Primary author + analysis support

## Context

The repository currently maintains two parallel domain modeling implementations:

- **V2** (`Poly/Data/Modeling`): The current production implementation. Uses internally mutable records protected by a large Command/Intent transactional layer (~65 command types, ~50 JSON intents, `Domain.Mutation` builder, per-Domain lock, custom rollback helpers). This delivers strong immediate value for MCP/LLM agents via `ApplyWithTrace` (atomic changes, automatic rollback on analysis errors, rich traces, sessions).
- **V3** (`Poly/DomainModeling`): A cleaner implementation using true immutable records (`Domain`, `Entity`, `Stage`, etc. as `sealed record`s with `IReadOnlyList` children) and fluent builders. Models implement `Node` and reuse the shared `Poly/Syntax/Analysis` infrastructure. Includes a powerful unified `DomainExpression` system.

Two independent cost-benefit analyses (engineering downstream consequences + consumer/product flexibility) plus explicit review concluded that the mutation layer in V2 concentrates growing incidental complexity ("mutation tax") as the model evolves.

## Decision

We adopt the **immutable record core** (V3 shape) as the long-term target for all domain model types.

**Non-negotiable constraint:** The model evolution pattern must be preserved. Model correctness (analysis + validation + atomic application with full rollback on error + rich traces) remains a hard requirement for consumers, especially LLM/MCP agents and code generation pipelines.

We will re-express the transactional evolution experience as a **thin evolution layer** over immutable roots rather than abandoning it.

## Rationale

- **Long-term maintainability:** Immutable records eliminate the majority of the bespoke mutation plumbing, private mutable state, locks, and duplicated intent/command boilerplate.
- **Downstream benefits:** Snapshots, branching, concurrency, persistence, analysis, and reproduction become dramatically simpler and more reliable.
- **Future capability:** Enables cheap domain forking, cleaner runtime instance semantics, and better support for simulation/execution engines.
- **Alignment:** Matches core project principles (domain model as the primary artifact, reuse of Syntax/Analysis, builders over premature heavy abstraction, minimal incidental complexity).

## Consequences

- `Poly/DomainModeling` becomes the source of truth for the core model shape.
- `Poly/Data/Modeling` (V2) will remain the integrated surface during a transition period while the thin evolution layer and dependent systems (MCP tools, demos, lowering, analyzers) are migrated.
- A new thin "evolution applicator" must be built that provides equivalent `CreateMutation` / `ApplyWithTrace` semantics (with identical or better traces and rollback behavior) on top of immutable instances.
- All major consumer surfaces (MCP agent tools, lowering to contract interfaces per AGENTS.md rules, the three demo domains, tests) must be migrated while preserving observable behavior.
- Short-term dual maintenance is accepted. Long-term maintenance cost for domain modeling should decrease significantly.

## Related Documents

- Detailed decision record: `docs/decisions/2026-05-31-immutable-core-detailed.md`
- Evolution layer design: `docs/decisions/2026-05-31-evolution-layer-design.md`
- Full V2 → V3 porting plan (internal session artifact)

## Next Steps

See the detailed decision record and the approved porting plan for the phased migration approach (evolution layer first, then analysis/lowering parity, MCP migration, demo migration, eventual cutover). 

The documented roadblocks (cross-entity effects, dynamic calculations, ownership constraints) should be used as forcing functions during the port.