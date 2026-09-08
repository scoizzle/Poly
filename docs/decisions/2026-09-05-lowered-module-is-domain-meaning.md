# ADR: Lowered operation module is domain meaning

**Date:** 2026-09-05  
**Status:** Accepted (agent-facing lock)  
**Deciders:** Primary author  

**Related:** [`2026-09-04-frozen-core-pipeline.md`](2026-09-04-frozen-core-pipeline.md) · [`2026-08-15-domain-library-extensions-mcp-harness.md`](2026-08-15-domain-library-extensions-mcp-harness.md) · [`docs/CORE.md`](../CORE.md) §0 · [`AGENTS.md`](../../AGENTS.md) Frozen core + Agent target · [`docs/plans/pipeline-transformation-2026-09-04.md`](../plans/pipeline-transformation-2026-09-04.md)

## Context

Agents and dogfood tests treat `DomainEntityInstance` + MCP `create_instance` / `invoke_action` / `link_instances` as proof that a domain is implemented. That path is a **scratch bind**: dictionary `This`, string `CurrentStage`, store edges the module does not name, and leftover execute-time `LowerActionBody` for subscriptions / transition batches / per-call policy lower.

The frozen pipeline already says otherwise. Stage 3 (`session.Lower`) produces the operation module. Stage 5a (execute) and 5b (print) consume **that module**. Scratch store and C# `Stay.Create` are current consumers, not architecture.

Hotel dogfood made the split visible: a DEI walk can go green while generated C# last-writer occupancy, unwired `Create`, throwing path-prefix `require`s, and partial Occupy-then-invoke mutation disagree. `*_Export_Compiles` only Roslyn-checks print. A DEI `Hotel_Runtime_*` never constructed a generated `Guest`.

Growing the harness (more store jobs, more MCP theater, more bag preludes) makes fake implementation cheaper than finishing lowering. That is the wrong attractor.

## Decision

**The domain is the lowered operation module.** Every shipped action, policy, subscription, create, and transition is a complete Syntax tree in `session.Lower`. Simulate and C# print run or print **those trees**. Host files **call** the module; they do not rewrite operations.

`DomainEntityInstance` / `DomainInstanceStore` remain the current scratch bind for dictionary `This` + Store (MCP harness). They are not a second interpreter, not the customer API, and not product-surface proof.

A construct “works” when:

1. It lowers into the module (fail closed if it cannot).
2. The same body executes (VM on bound `This`, or the generated CLR method).
3. Print of that body matches the execute semantics (no consumer-only flag, no Effect-IR walk as shipped meaning).

When simulate and emit disagree, **fix lowering** (one tree). Do not special-case the bag runtime or MCP `link_instances` to paper over a missing tree.

### Agent rules

1. **Do not add product behavior** as a DEI host prelude, Effect-IR walk, MCP-only wiring, or `store.Link` the module does not name.
2. **Do not treat a green DEI/MCP walk as T2 / customer proof.** Label harness tests as harness. Product-surface tests construct and call generated types, or execute cached module bodies on a bound directory.
3. **Remaining execute-time `LowerActionBody`** (subscriptions, transition batches) and per-call `EvaluatePolicy` lower are **debt**: they belong in the module at `session.Lower`, not at invoke.
4. **Do not grow MCP theater** (new simulate tools, more instance-store semantics) as the place domain meaning “actually happens.”
5. **Shrink the language** if the next construct cannot lower. Do not ship a keyword whose implementation is a harness escape.

## Consequences

- Always-on: [`AGENTS.md`](../../AGENTS.md) **Agent target**. CORE hard line: lowered module is domain meaning.
- Pipeline transformation P1–P6 landed a one-tree named-action path. This ADR forbids treating the residual harness as the product path.
- University / CRM / Hotel DEI dogfood stays valid **harness smoke** until module-execute / generated-type tests exist. Do not delete them in this decision; do not extend them as if they were the C# API.
- Deleting `DomainEntityInstance` is **not** this change (frozen ADR non-goal). Replacing it is a planned slice through frozen seams after the module is complete.

## Non-goals

- Admitting a PIPELINE-STATUS CURRENT suite in this change.
- Rewriting Hotel occupancy in DEI to match export (wrong layer).
- EF / HTTP / CLI as the proof surface (doors map the catalog; they do not own operations).
