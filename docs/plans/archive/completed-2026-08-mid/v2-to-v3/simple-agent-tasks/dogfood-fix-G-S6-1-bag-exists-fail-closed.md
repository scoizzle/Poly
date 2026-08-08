# Fix G-S6-1 — Bag-path relationship-named `Rel exists` fails open

**Queue:** [`dogfood-fix-README.md`](dogfood-fix-README.md)  
**Source:** [`DOGFOOD-S6-20260806.md`](../agent-summaries/dogfood/DOGFOOD-S6-20260806.md) finding G-S6-1  
**Status:** `[x]` — DONE 2026-08-06 (fix + regression test + S6 re-run green)  
**Diff:** S–M  
**Failure mode:** fail-open (violates fail-closed + guide drift)

---

## Problem

`evaluate_policy(entityName, policyName, properties=…)` without `instanceId` evaluates
against a **local subject bag**. For a relationship-named `Rel exists` (e.g. `loans exists`,
`not loans exists`), the bag path does not throw "requires store" as the guide promises —
it fail-opens to **true** (`loans != null` lowering → VM missing member → non-null → true).

Observed (S6, call 11): `NoLoan: policy { not loans exists }` with `properties: {"Name":"Bob"}`
→ `false` — i.e. `loans exists` → `true` with **no store links at all**.

Guide claim (Poly.Mcp/Docs/poly-dsl-guide.md §9, standalone bag): "relationship-named
`Rel exists` requires store (**throws without it**)."

## Root cause (verified by investigation)

Bag path creates the instance via `DomainEntityInstance.Create(entity, subjectValues)`
with no `Domain`/store context. `TryEvaluateRelationshipPresence` bails to bag lowering,
where `Exists(PropertyAccess("loans"))` lowers to `loans != null` → VM missing-member →
non-null → **true** (fail-open).

## Exact Steps

1. Add a failing TUnit test (Method_Condition_ExpectedResult) that evaluates
   `Rel exists` / `not Rel exists` on a bag-created instance (no store, no links) and
   asserts it **throws** (guide-consistent) — not true/false.
   - Also cover store-linked path unchanged: `exists`/`not exists` still evaluate against
     links (empty → false / true).
2. Fix: in the relationship-presence evaluation, when the relationship name refers to an
   outbound **relationship** (domain metadata known) and the instance is **not store-bound**
   (bag path), throw a clear "requires store + links" error instead of lowering to a
   bag property access. Non-relationship `Exists(PropertyAccess)` (bag-null-lowering) is
   unchanged (guide keeps that for local properties).
3. Keep the store-linked path exactly as-is (S6 PASS evidence: empty → false/true; linked →
   true/false; quantifiers unaffected).
4. Update DSL guide only if wording drifts; verify existing §9 wording matches final behavior.

## Implementation (DONE)

**Fix (smallest, at the MCP boundary — `Poly.Mcp/Tools/DomainTools.cs`):**
`PolicyTool.EvaluatePolicy` bag branch now passes the session domain into the instance:
`DomainEntityInstance.Create(entity, subjectValues, state.Domain)`. The bag instance is
now domain-bound, so `TryEvaluateRelationshipPresence` resolves the name against real
relationship metadata and reaches `GetOutboundRelatedInstances` → `Store is null` →
**throws** `"Cannot resolve relationship target without a DomainInstanceStore"`.
Non-relationship names (`Name exists`) still miss in `TryGetRelationship` → bag-null
lowering preserved.

Rationale (why not a nav-property check in `TryEvaluateRelationshipPresence`):
navigation properties are stored as `Relationship`s on `Domain.Relationships`, NOT as
entity `Property` entries — so a standalone instance (`Domain == null`) has no way to
distinguish a relationship name from a local property. Passing the domain is the only
reliable way to know, and it reuses the existing store-missing throw (consistent with
the G1 precedent: `Success: false` + diagnostic text).

**Regression test:** `EvaluatePolicy_BagMode_RelExists_FailsClosedWithoutStore`
(Poly.Tests/Mcp/SurfaceExtensionDogfoodTests.cs) — bag `profile exists` / `not orders exists`
→ `Success false` + message contains "store"; bag `Name exists` (non-relationship) still
evaluates without a store.

**Verified on the real MCP server** (`/private/tmp/poly-dogfood/gs61.json` + s6 re-run,
`gs61.out` / `s6-fixed.out`):
- gs61 call 4/5: `HasProfile`/`NoOrders` bag → `success:false`, "Cannot resolve relationship
target without a DomainInstanceStore".
- gs61 call 2/3: `HasName` bag → still evaluates (`true`), no store required.
- s6 call 11 (bag probe): now `success:false` "requires store" — previously fail-opened
to `loans exists → true`. All store-linked calls (6, 20, 21–26) still PASS unchanged.

## Definition of Done

- [x] Bag `Rel exists` / `not Rel exists` throws (fail-closed) instead of returning true/false
- [x] Store-linked `Rel exists` / `not Rel exists` still correct (empty → false/true; linked → true/false)
- [x] Non-relationship bag `Exists(PropertyAccess)` unchanged (bag-null-lowering preserved)
- [x] New TUnit tests green; full Poly.Tests suite green (1841/1841)
- [x] Guide §9 wording matches observed behavior (unchanged: "relationship-named `Rel exists`
  requires store (throws without it)" — now true; `GetDslGuide_ReturnsProductSurface` green)
- [x] S6 scenario re-run: bag probe now throws (documented above), store path still PASS

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

Re-run S6 bag probe via MCP harness; confirm `NoLoan` bag eval now errors (isError/throws),
store-linked eval unchanged.
