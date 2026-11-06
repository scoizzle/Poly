# Identity spike — status

**Date:** 2026-11-06 · **Branch:** `feat/ontology-agent-ux` · **Base:** `aa142413`

## Question

Can Poly expose an instance identity that a lowered module can read, so `key(parent, value)` / `unique_within(parent)` can scope by the parent’s identity?

## What the probe proved

Two tests against the real `ModuleInstanceExecutor` (`Poly.Tests/DomainModeling/Runtime/IdentitySpikeTests.cs`):

1. `Identity_IsReadableFromTheInstanceBag_WithoutAModuleMember` — `TryFind(bag)` returns the instance and its `Id`; `TryGet(id)` returns the same bag. **The runtime already has identity; it is host-visible, not module-visible.**
2. `Host_CanComputeAndMoveAScopedCompositeKey` — a host can compute `(parent.Id, value)`, see it collide under the same parent, distinguish a different parent, and release/re-record it on `Unlink` → `Link`. **The runtime half of a scoped key works today.**

## What is missing

The lowered module cannot read the identity:

- the create guard is emitted on the **static** `Create` factory, so there is no `this`;
- the printed entity has **no `Id` member** (62 entity goldens, 0 `HasOne`/`HasForeignKey`, 1 EF `HasKey` on a shadow `int`);
- the interpreter mints the GUID **after** `Create` and keeps it only in `_byId`/`_byBag`;
- link/unlink mutate the bag and never touch the storage port.

## Blast radius

| Change | Sites |
|---|---|
| Entity `.cs.golden` files | 62 |
| `Create(` calls in goldens | 78 |
| `new <Entity>(` calls in goldens | 70 |
| `HasOne` / `HasForeignKey` in core + packs | 0 |
| `HasKey` (EF shadow key) | 1 |

## Options, priced

| Option | Module read at create/assign | Print/EF cost | Link/reparent |
|---|---|---|---|
| **A. Universal `Id` member** | yes (`parent.Id`, `this.Parent.Id`) | 62 goldens, 78 create calls, 70 ctors; EF key/route identity changes | needs key release/re-record |
| **B. Host scope token on scoped entities** | yes, as an implied parameter | narrower: only entities with scoped keys/parents; changes those create signatures | same |
| **C. Natural-key navigation scope** | yes, read `parent.NaturalKey` (domain scalar) | no identity member; parent must have a unique immutable key | same |
| **D. Scalar scope (`key(parentId, value)`)** | yes, `parentId` is a constructor parameter | none beyond the composite index | key invariant; nothing to move |

## Verdict

Identity is **runtime-ready**; the blocker is the print/EF/route surface, not the VM. Option A is a product decision (every entity gains identity, routes and EF keys change), not a prerequisite for scoped uniqueness. Ship D first (enforceable now), then C (natural-key nav scope) if the meta-model needs the nav spelling; reserve A for the identity/foreign-key feature that also unlocks references and graph edges.

The spike tests stay as the regression proof that identity is available to the host and that a scoped key can move.
