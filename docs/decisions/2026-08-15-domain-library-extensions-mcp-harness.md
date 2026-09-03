# ADR: Domain is a library; extensions bind doors; MCP is the harness

**Date:** 2026-08-15  
**Status:** Accepted (agent-facing lock)  
**Deciders:** Primary author  

**Related:** [`2026-08-14-domain-libraries.md`](2026-08-14-domain-libraries.md) · [`2026-06-08-domain-lowering-boundary.md`](2026-06-08-domain-lowering-boundary.md) · [`2026-07-11-platform-trust-bar-and-dogfood.md`](2026-07-11-platform-trust-bar-and-dogfood.md) · [`2026-07-22-persistence-units-medium-facets-pack-syntax-export.md`](2026-07-22-persistence-units-medium-facets-pack-syntax-export.md) · mechanisms in [`docs/CORE.md`](../CORE.md)

## Context

A domain is business logic: entities, stages, actions, policies, subscriptions. That is a **library of legal operations**, not a process with a known `Main`. Agents and hosts kept filling the missing door — `Comment` nodes, a second effect interpreter, `Program.cs` from the compiler, MCP tools that invent CRUD — and treating a runnable host as proof the domain lowered.

Interpretation (AST → analyze → VM / `CSharpGenerator`) is the execution backbone for **named operations** and for known algorithms. It is not “run the domain.”

## Decision

### 1. Domain lowers to a module, not a process

After analysis, each shipped action, policy, create, and subscription body has a **complete, legal** Syntax AST (generic ops only — no domain VM opcodes).

- **Complete:** no `Comment`, no `null` from lowering, no host tree-walk beside the VM, as *shipped meaning*.
- **Legal:** what the domain forbids is in that tree (guards), not only in a later factory or instance method the export might skip.
- **No required entry point.** The author may be modeling only business logic. Capability / catalog is the menu of operations, not `main`.

Runtime and emit of those operations consume **the same** lowered trees. Consumer flags that change lowering (`LowerStageTransitions` and the like) are forbidden in new work.

### 2. Shipped language ⊆ lowerable language

A construct is shipped only if it lowers to that tree and program analysis of the tree is clean. New spell waits in `docs/plans/` (and stays out of the parser and the DSL guide) until then.

AGENTS §5 (thinnest slice) may **shrink the language**. It may not ship a keyword whose implementation is optional.

Residual dual-path in the tree (`EffectExecutor`, `ExecuteStructured`, preprocess-to-literal, compiler-always-MinimalAPI) is **debt**. Do not grow it. Do not call a path done because parse or one consumer worked.

### 3. Opt-in extensions bind doors and projections

Libraries (formerly “packs”) load because the domain listed `uses` ids. One type: `Id` + `Register` on the session. They do not add dialects.

| Job | Examples | Produces a process door? |
|-----|----------|--------------------------|
| Meaning | `temporal` (`Now`, `12 days`) | no |
| Persistence / facets | `storage`, `sqlite`, `sqlserver` | no |
| Product host | REST / HTTP, later gRPC, workers | **yes** — binds already-lowered operations |
| Another domain | `ImportedContract` | not an extension id |

Core seed is the closed language plus the entity module. It does **not** emit `Program.cs`. A REST (or other) surface appears only if that extension is loaded. CLI flags **seed ids** (`--dbms sqlite` → `uses sqlite`). They do not bypass the catalog or imply a host.

A host extension maps named operations onto routes (or equivalent). If the operation did not lower, the extension fails closed. It does not complete missing lowering in strings.

### 4. Poly.MCP is the interactive harness

MCP is how agents **use Poly** in a conversation. It is not the domain session and not a product entry-point extension.

| MCP does | MCP does not |
|----------|----------------|
| Hold a `DomainSession` + revision + scratch store | Invent domain or execution semantics |
| Author (`apply_dsl` / evolve) and inspect (catalog, diagnostics) | Infer a `Main` or ship the customer API |
| **Simulate** a named policy or action on a store instance (`create_instance` then `evaluate_policy(instanceId)` / `invoke_action`) | Run “the domain”; use a second evaluator; treat `oracle_expression` as named-policy simulate |
| Return a fact diff or reject | Treat `Comment` / host-only effect dispatch as success |

Simulate and product emit must agree. The executing program is the **lowered implementation** — it does not still know the domain model; lowering already consumed facts and bags. The store instance replaces the missing entry point: the agent names the operation and supplies `create_instance` (+ `link_instances` when related).

Interpretation is the backbone of that simulate (and of cataloged algorithms). Domain runtime is store + **run this program**.

### 5. Host ABI stays tiny

Remaining store and clocks (`Create` / create-in / time/id) are `CallExternal` (or equivalent). Notify, stage, and invoke are Syntax — not host-ABI `CallExternal`. No `OpCode.CheckPolicy` / `StageTransition`.

## Consequences

- 2026-08-25: §5’s CallExternal list is create/time only. Stage, Notify, self/cross-entity invoke, and for-invoke already lower to Syntax (`Assignment` + `Invoke(Member(This, "Notify"))`, `Invoke(Member(This, action))`, fail-fast `ForEachLoop`). Do not read “store and clocks” as covering those.
- Agents edit Domain and extensions. They do not choose an execution path.
- New DomainExpression / Effect kinds require a real lowered node in the same change.
- New product doors are libraries, not core compiler special cases.
- MCP tools stay honest: descriptions match simulate-on-lowered-AST (or the tool is not claimed).
- Mechanisms: [`docs/CORE.md`](../CORE.md). Do not restate this ADR as a plan.

## Non-goals

- Byte-identical replacement of `Poly.dll`.
- Requiring every domain to opt into HTTP.
- Finishing C# in the VM before algorithm catalogs or domain lowering.
- Treating MCP invoke as the shipped customer API.
