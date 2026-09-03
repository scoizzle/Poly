# ADR: Facts, concern bags, and Store bind

**Date:** 2026-09-03  
**Status:** Accepted  
**Deciders:** Primary author  

**Related:** [`docs/CORE.md`](../CORE.md) · [`2026-08-15-domain-library-extensions-mcp-harness.md`](2026-08-15-domain-library-extensions-mcp-harness.md) · [`2026-07-22-persistence-units-medium-facets-pack-syntax-export.md`](2026-07-22-persistence-units-medium-facets-pack-syntax-export.md) · [`2026-06-08-domain-lowering-boundary.md`](2026-06-08-domain-lowering-boundary.md)

## Context

Domain analysis already decomposes a `Domain` into concern bags (`StorageMappingMetadata`, persistence/HTTP surfaces, catalog, dispatch plans). File emit consumes those bags. Operation lowering largely does not: unique-before-mutate lived as a host prelude on `DomainEntityInstance` (`UniqueCollisionForAssign` / `ExecuteStructured`) because `DictSetItem` does not talk to the scratch store, and create-in auto-link still walks Effect IR.

The wrong fix is another lowering flag or a second interpreter. The host surface was wrong: uniqueness and graph wiring are Store jobs that leaked onto the instance.

## Decision

Use these words. Do not invent a framework catalog.

| Term | Meaning |
|------|---------|
| **Facts** | `Domain` — types, relationships, operations, `uses` ids |
| **Concern** | Derived view (storage mapping, persistence surface, HTTP, catalog, dispatch, …) |
| **Bag** | Analysis metadata for a concern (`StorageMappingMetadata`, …) |
| **Surface** | Opt-in door that *selects* an implementation (`uses sqlite` → persistence surface) |
| **Store** | Named **collaborator** the operation AST invokes (`EnsureUnique`, later `Create` / `CreateIn`) |
| **Bind** | Host supplies the collaborator (scratch `DomainInstanceStore`, later EF). Caller-supplied; **not** `Storage.Default`, not a DI container in the VM |
| **Lower** | Operation → Syntax. The **process** reads bags (and facts only when a bag is absent). The **product** is generic Syntax that invokes bound collaborators — **no bag types, no bag metadata, no Domain re-scan in the tree**. No Effect-IR walk. |
| **Project** | C# print of that same tree (still no bags) |
| **Host files** | Bag-gated adapters that *are* the bound implementations (DbContext, Program.cs). Bags stay here, not in the operation AST. |

**Storage** already means the mapping bag (`StorageModel` / `StorageColumn`). The collaborator is **Store**. Do not name it `IStorage`.

**Duties** (who may know bags):

| Duty | Owner | Must not |
|------|-------|----------|
| Hold facts | `Domain` | Encode persistence, HTTP, or Store |
| Publish concern bags | Analysis | Execute or print operations |
| Select an implementation | Surface (`uses sqlite`, `uses http`, …) | Rewrite operation trees |
| Read bags; emit Syntax | Lowering (**process**) | Embed bag types in the tree; walk Effect IR |
| Be the program | Operation AST (**product**) | Name or read bags |
| Print that program | Project | Consult bags to understand the action |
| Bind / be the implementation | Store + host files (DbContext, Program.cs) | Re-walk `Domain` for operation meaning |

Constraint checks (required, pattern, range) can stay on the entity factory. Uniqueness and graph wiring belong on Store.

Scratch `DomainInstanceStore` and a future EF Store implement the same Store jobs. Do not invent a third store.

A lowering flag or host Effect-IR walk means the bag/collaborator was not bound.

## First slice (this change)

Unique `AssignEffect` lowers to `Invoke(Member(This, "EnsureUnique"), property, value)` plus the same `DomainResult.Failure` rewrap as create-in / invoke. `DomainEntityInstance.EnsureUnique` delegates to the bound `DomainInstanceStore` (Notify-shaped: dictionary-backed `This` cannot Member-read `Store`). Lowering prefers `StorageMappingMetadata` columns (`IsUnique`) and falls back to `UniqueConstraint` when the bag is absent.

C# unique remains the persistence-surface concern (EF indexes from `StorageColumn.IsUnique`) until an EF Store exists. That split reuses the existing runtime-vs-export create split (`LowerStageTransitions`); it is not a new consumer flag. Residual dual-path is debt.

Not this slice: `Store.Create` / `CreateIn`, deleting `ExecuteStructured`, unifying `Stay.Create` vs `CreateByType`.

## Consequences

- Unique-inside-`if` compiles as one VM tree. Failure from nested `Return` must surface as `DomainResult`. If it does not, fix lower/VM result mapping — do not restore the walker.
- The operation AST never names a bag type. If project needs `StorageMappingMetadata` to print an action body, the collaborator was not bound.
- Heuristic for later work: if emit of a Store job is hard, the host surface is wrong.
