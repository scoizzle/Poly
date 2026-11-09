# Domain identity is a compile-time key designation (#405)

Status: **transcription of a decision already made 2026-11-06; this file adds no new decision.** Recorded
2026-11-09 because [issue #405](https://github.com/scoizzle/Poly/issues/405)'s final acceptance item asks
for it: *"The decision above is recorded in `docs/decisions/` with the route/EF consequence."* The sources
are quoted verbatim below; prose between quotes is this file's framing only. Where a clause is absent
from the sources it appears under *Open questions* rather than being filled in.

This decision supersedes Option A (a universal printed `Id` member), which was never shipped.

## Sources (verbatim)

### Source A — the decision comment

[scoizzle/Poly#405 comment](https://github.com/scoizzle/Poly/issues/405#issuecomment-6696309543),
id `IC_kwDOAPVu2s8AAAABjyGTJw`, by scoizzle, 2026-11-06T21:37:09Z. The issue body names this comment as
the decision comment: "Decision comment: [#405 comment](https://github.com/scoizzle/Poly/issues/405#issuecomment-6696309543)."

> ## Decision: key-as-designation (2026-11-06)
>
> Identity is a **compile-time designation**, not a runtime member. This supersedes Option A (universal `Id` member) as the recommendation.
>
> - **Analysis:** `KeyProperties` metadata; `Id` is an alias for the designated tuple (or the single property). It types references and checks stability; it is not a domain node.
> - **Lowered code:** the designated properties (or their tuple) directly. No synthetic `Id` member, no 62-golden re-baseline.
> - **Runtime:** key values live in the bag; the executor GUID remains the **instance handle**, not domain identity.
> - **Storage/EF:** composite key or alternate key; `HasPrincipalKey`/`HasForeignKey` when a nav is part of the key.
> - **Keyed vs unkeyed:** an entity with no `key` has no domain identity; it can still have a handle but cannot be a key scope or FK target.
> - **Admission:** non-empty, distinct, required, immutable properties; a navigation in a key must be singular and its target must have a key; keys resolve recursively and cycles are rejected.
> - **Naming:** `key` = identity; `unique` = alternate at-most-one; `unique_by` = collection-scoped.
>
> The identity spike’s host-visible id stays as the instance handle. The remaining work is the key metadata and recursive analysis, the composite index/EF FK, link/unlink/reparent key maintenance, and route/schema emission. No universal `Id`.

### Source B — the issue body's decision text

Same issue, same author, same date. Quoted because it states clauses Source A compresses: the `key(a, b)`
surface spelling, the routes consequence, and the extended admission list.

`## Summary`, the decision paragraph:

> **Decision (2026-11-06): identity is a compile-time key designation, not a runtime member.** `key(a, b)` designates existing properties as the entity's identity; analysis carries `KeyProperties` (with `Id` as an alias for the tuple or the single property); the lowered code uses those properties directly, with no synthetic `Id` member and no golden re-baseline. The executor GUID stays an **instance handle**, not domain identity.

`## Decision: key-as-designation`, in full:

> ## Decision: key-as-designation
>
> | Layer | With key-as-designation |
> |---|---|
> | Analysis | `KeyProperties` metadata; `Id` is an alias for the designated tuple; types references, checks stability |
> | Lowered C# | the designated properties (or their tuple) directly; no synthetic `Id` |
> | Runtime | key values live in the bag; the executor GUID is only the instance handle |
> | Storage/EF | composite key or alternate key; `HasPrincipalKey`/`HasForeignKey` when a nav is part of the key |
> | Routes | declared key when one exists; the handle otherwise |
>
> An entity with no `key` has **no domain identity**: it can still have an instance handle, but it cannot be a key scope or a foreign-key target. Keys resolve recursively through singular navigations; a cycle is rejected.
>
> Admission rules:
>
> - non-empty, distinct, required, immutable properties;
> - no `optional`, no collections, no entry-assigned key members;
> - a navigation in a key must be singular and its target must have a key;
> - a keyed entity's key cannot be reassigned in v1 (a future key edit is a re-index, not a silent move);
> - `key` = identity, `unique` = alternate at-most-one, `unique_by` = collection-scoped.

### Source C — the 2026-11-09 progress comment

[scoizzle/Poly#405 comment](https://github.com/scoizzle/Poly/issues/405#issuecomment-6746907374),
id `IC_kwDOAPVu2s8AAAABkiWi7g`, 2026-11-09T17:32:51Z, records that slice 1 is complete via merged PRs
#440 and #443 and that *"the decision this epic relies on is recorded only as an issue comment here, not
as a `docs/decisions/` file"* — the gap this file closes. It decides nothing further.

## The decision, in the sources' own clauses

- Identity is a **compile-time designation**, not a runtime member (A, B).
- `key(a, b)` designates **existing properties** as the entity's identity (B).
- Analysis carries `KeyProperties`; `Id` is an **alias** for the designated tuple, or for the single
  property; it types references and checks stability, and it is **not a domain node** (A, B).
- Lowered code uses the designated properties (or their tuple) directly: **no synthetic `Id` member**,
  no golden re-baseline (A, B).
- Key values live in the **bag**; the executor GUID is only the **instance handle** (A, B).
- An entity with no `key` has **no domain identity**: it may have a handle, but it cannot be a key scope
  or a foreign-key target (A, B).
- Keys resolve **recursively through singular navigations**; a cycle is rejected (A, B).
- Admission: non-empty, distinct, required, immutable properties; a navigation in a key must be singular
  and its target must have a key (A, B); no `optional`, no collections, no entry-assigned key members (B);
  a keyed entity's key cannot be reassigned in v1 — a future key edit is a re-index, not a silent move (B).
- Naming: `key` = identity, `unique` = alternate at-most-one, `unique_by` = collection-scoped (A, B).
- Storage/EF: composite key or alternate key; `HasPrincipalKey`/`HasForeignKey` when a nav is part of the
  key (A, B).
- Routes: the declared key when one exists, the handle otherwise (B).

## Routes and storage/EF consequence

The acceptance item asks for the route/EF consequence, and this is it in the decision's own words:
storage emits a **composite key or alternate key**, with `HasPrincipalKey`/`HasForeignKey` when a
navigation is part of the key (A, B); routes use the **declared key when one exists, the handle
otherwise** (B). A keyed entity's key is a re-index target, never a silent move (B).

## What is implemented today (slice 2, head `984f39c7`)

Slice 2 landed the designation as **metadata over existing members**. Nothing is enforced at runtime and
no surface form exists yet.

- `Entity.KeyProperties` — an internal `IReadOnlyList<string>` of the entity's own property or singular
  navigation names; empty means unkeyed (`Poly/DomainModeling/Ontology/Entity.cs:50`). Internal because
  *"nothing enforces a key at runtime yet, so embedders must not set one"*.
- `EntityStructureMetadata.KeyProperties` plus `Id` as the alias (the tuple in declared order) and
  `HasKey` (`Poly/DomainModeling/Analysis/EntityStructureMetadata.cs:84-99`). Empty `Id` for an unkeyed
  entity.
- Names are **scoped to the entity**: two entities may each key on `Code`
  (`Poly/DomainModeling/Analysis/StructuralDomainAnalyzer.cs:80-90`).
- Admission is enforced as analysis diagnostics: a blank or unknown member and an `optional` or
  effect-assigned key property are `DMSTR007` (`InvalidKeyDesignation`); a repeated member is `DMSTR001`
  (`StructuralDuplicate`); a key navigation that is a collection, targets a non-entity, or targets an
  unkeyed entity is `DMSTR007`; a key cycle, including a self-navigation, is `DMSTR002`
  (`StructuralCycle`) (`Poly/DomainModeling/Analysis/StructuralDomainAnalyzer.cs:91-152`,
  `Poly/DomainModeling/Analysis/DomainModelDiagnosticCodes.cs:4,5,35`).
- Printing and export are unchanged: a keyed entity is **byte-identical** to its unkeyed twin
  (`Poly.Tests/DomainModeling/Analysis/EntityKeyDesignationTests.cs:67`). No member is lowered or printed.

## What is not built

- The `key(...)` **authoring surface** — parser and printer. The issue puts it last: *"The parser arm
  lands **last**, so nothing is authorable before it is enforced."* Every slice-2 test builds the AST/API
  directly.
- Composite key enforcement at create and assign over the designated tuple; the composite index;
  `HasPrincipalKey`/`HasForeignKey` for navigation keys; declared-key route keys (issue slice 3).
- Link/unlink/reparent key lifecycle — link check/record, unlink release, reparent atomic move (slice 4).
- Scoped/alternate **value** uniqueness (issue slice 5). No `unique_by` or `unique(scope, value)` form is
  implemented; `unique_by` appears nowhere in `Poly/`.
- `x-poly-key` in JSON Schema, and `poly_check` reporting that it cannot prove instance keys (slice 6).
- Runtime key enforcement of any kind: the designation is analysis metadata only.

## Open questions (the sources do not decide these)

Flagged, not interpreted; each is quoted where it can be read two ways.

1. **Scoped-uniqueness spelling.** Source A's naming clause says `unique_by` = collection-scoped, while
   Source B's slice 5 says *"a scoped/alternate `unique(scope, value)` is a separate constraint"*. Both are
   the same date and author. Which form is authoritative is unstated.
2. **Where cycle rejection lives.** Sources say *"a cycle is rejected"* / *"cycles are rejected"*; no layer
   (analysis, lower, runtime) and no diagnostic is named, though slice 2 implements it as `DMSTR002`.
3. **Whether `Id` is readable from authoring code.** Source A says the alias *"is not a domain node"* and
   Source B says there is *"no synthetic `Id` member"* and no entity *"gains a member"*. Whether a `.Id`
   expression in the DSL should bind at all is not stated.
4. **`v1` scope for key reassignment.** Source B says *"a keyed entity's key cannot be reassigned in v1
   (a future key edit is a re-index, not a silent move)"*. Which slice is `v1`, and what the failure looks
   like, is not stated.
5. **Entry-assigned vs immutable.** Source B forbids *"entry-assigned key members"* and Source A requires
   key properties to be *"immutable"*. Whether a create-time initializer is "entry-assigned" (forbidden) or
   simply the initial value (legal) is not stated; slice 2 admits a create-time default and rejects
   assignment by an action or by a first-stage entry effect
   (`Poly.Tests/DomainModeling/Analysis/EntityKeyDesignationTests.cs:131,215,226`).
6. **Unkeyed route key.** Source B's routes row says *"the handle otherwise"* but does not say how a handle
   route is formed or namespaced.
7. **Performance criteria are not decision clauses.** *"create overhead is under 5%"* and *"`EXPLAIN QUERY
   PLAN` uses it"* are acceptance checks on the issue, not statements about the model, and are recorded
   here only as acceptance criteria.

## Acceptance item this file satisfies

> The decision above is recorded in `docs/decisions/` with the route/EF consequence.

The route/EF consequence is the *Routes and storage/EF consequence* section above, in the decision's own
words; the decision itself is Sources A and B, quoted verbatim.

## Consequences

- The epic's documentation blocker (Source C) is closed; this file records no new ruling and changes no
  behaviour.
- The `key(...)` surface remains unauthorable until the parser slice lands and the enforcement slices make
  the designation authoritative.
- `docs/decisions/README.md` is not updated by this change, and `AGENTS.md` carries no always-on line for
  it; both are left for the slice that makes the designation authorable.
