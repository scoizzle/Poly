# P1 temporal — design lock (research spike output)

**Date:** 2026-08-06  
**Status:** **Suite complete** (2026-08-13). Implementation suite done: [`simple-agent-tasks/p1-README.md`](simple-agent-tasks/p1-README.md) — DONE; gate [`simple-agent-tasks/p1-gate.md`](simple-agent-tasks/p1-gate.md) `[x]`. 2147/2147 green. Authoring/analysis/print-round-trip shipped; runtime clock eval (design-lock Q4 fixed `TimeProvider` seam) is a recorded follow-up, not shipped.

**Pre-ship 2026-08-13 (🟡 filed, not blocking authoring):** shipped create/assign defaults use local `DateTime.Today` for `today`; VM/export lowering of a `Today` node still emits `DateOnly.FromDateTime(DateTime.UtcNow)`. Reconcile both to `TimeProvider` when Q4 ships. `ExpressionTypeAnalyzer.CategoryOf` still classifies IR property types `Time`/`Duration` as Date; DSL does not author those as primitives (`Hold: Duration` is a nav). DateOperation analysis now rejects them as date operands.  
**Prereq:** Grammar GI + **E1** done (2026-08-07; plan archived under `archive/completed-2026-08-mid/grammar-integration.md`). Pack uses `ExpressionFormRegistry` / open forms **and matching print forms** ([`pack-host-2026-08-13.md`](pack-host-2026-08-13.md) wave 1; admit p1 as wave 3 after the host).  
**Source research:** [`p1-temporal-research.md`](p1-temporal-research.md) (Q1–Q5 answered 2026-08-06)  
**Parent vision:** [`domain-dsl-absorption-proposals.md`](domain-dsl-absorption-proposals.md) § P1 · experiment [`docs/plans/archive/experiments/DOMAIN-DSL-SPEC.md`](archive/experiments/DOMAIN-DSL-SPEC.md)

---

## Accepted direction (absorption §P1, 2026-08)

**Built-in temporal pack on core extension seams.** Temporal concepts are not hard-wired into core parser/`TokenKind` tables; a built-in temporal pack (always on the product path, like a standard library) is the **first real consumer** of (a) GI-4 pack grammar registration and (b) the absorption §2.5–2.6 specialization registry (units + binary op specializations).

**ADR lock (recommended at admit):** "Temporal authoring and specialization live in a built-in pack on core extension seams; resolved temporal IR lowers generically; scheduling is host."

## Inventory — what exists in the tree today (2026-08-06)

| Asset | Where | Notes |
|-------|-------|-------|
| `DateOperation(Date, Offset, Kind)` + `DateOperationKind { AddDays, AddMonths, DiffDays }` | `Poly/DomainModeling/DomainExpression.cs` (:177) | Core `DomainExpression` subtype; factory `DomainExpression.DateOp(...)` |
| Dispatch | `DomainExpressionDispatch.cs` (:27, :53) | Routed (coh-e1); new subtype = compile error |
| Rewrite | `DomainExpressionRewriteBase.cs` (:47) | Recurses Date + Offset |
| Lowering | `DomainExpressionLoweringPass.cs` (:195) | → `Invoke(Member(date,"AddDays"/"AddMonths"/"Subtract"), offset)`; unknown kind throws `NotSupportedException` (fail-loud) |
| `now`/`utcnow`/`today`/`guid` | `EffectLoweringPass.LowerDefaultExpression` (:375) | **`DefaultValueConstraint` string forms only** → `DateTime.UtcNow`, `DateOnly.FromDateTime(UtcNow)`, `Guid.NewGuid()`; no `Now` expression node in the policy/expression IR |
| DSL authoring | `PolyDslParser.cs` | **No** `days`/`months`/`weeks`/`Now` tokens — `DateOperation` is fluent-IR-only, not product-DSL-authorable |
| Guide | `Poly.Mcp/Docs/poly-dsl-guide.md` | "Not yet shipped: Date operations"; `invoke … where` rejects date expressions (filter = literals/comparisons/arithmetic only) |
| Tests | `DomainExpressionLoweringPassTests` (`DateOperation_AddDays/Months/DiffDays_LowersToInvoke`), `DomainEvolutionApplicatorTests` (`DomainExpression_DateOperation_CanBeStoredInAssignEffect`), `DomainExpressionVmExecutionTests` (`DateOperation_LowersWithoutThrowing`) | IR + lowering already exercised |

**Conclusion:** IR and lowering already model day/month arithmetic; builtins `Date`/`DateTime` and `now`/`today` exist. **Product DSL authoring and policy-eval clock honesty are incomplete — not the type system.**

## Decisions (research Q1–Q5, locked)

### Q1 — Core seed vs built-in pack + specialization registry

- **Contained vertical slice (revised 2026-08-13):** the built-in temporal pack owns **everything** temporal — the IR (`Now`, `Today`, `DateOperation`, `Duration` + enums), the parse forms (`NowForm`, `DurationForm`), the binary fold (`DateOperationFold`), the print binders + grammar patterns, the runtime/export default resolvers, and the pack class — all under `Poly/DomainModeling/Packs/Temporal/` (namespace `Poly.DomainModeling.Packs.Temporal`). Core never names pack types: `DomainExpressionDispatch` falls through to an open `ExpressionDispatchRegistry<TResult>` (ambient product-default set, populated by the pack's module initializer) for pack-owned subtypes; the analyzer routes temporal inference + checks and the runtime routes default resolution through the same registries. The `DomainExpression.DateOp` factory was removed (pack constructs `DateOperation` directly). This supersedes the earlier "DateOperation stays core as the resolved shape" phrasing — the IR is pack-owned, dispatched through the core `DomainExpression` seam.

### Q2 — Product-minimal authoring

- `Now - 12 days`, `DueDate + 14 days` as **assign RHS** (renew-style golden).
- **Compare** (`ExpiryDate < Now`) in policy guards.
- Out of vertical: `schedule at` (host P9), new date-default forms beyond existing `default now`/`today` strings.

### Q3 — Policy preprocess vs full lower

Same split as store-aware `Rel exists`:
- Store/policy path **preprocesses**: resolve `Now` once per evaluation via injectable clock; `DateOperation` stays stored IR.
- Export path **lowers fully** to CLR members (`UtcNow`, `AddDays`, `Subtract` — already implemented).
- No new dual path.

### Q4 — Host clock

- **CLR host uses `System.TimeProvider`** (repo is net10.0): default `TimeProvider.System`; tests inject `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing) or a small fixed subclass — no custom clock interface to invent or maintain.
- Domain IR stays **platform-agnostic**: `Now`/`today` are `DomainExpression` nodes; `TimeProvider` is the **CLR host adapter** only (built-in-type mapping rule: CLR mapping is one adapter; other hosts map the same IR to their own clock).
- `CreateTimer` covers P9 scheduling timers for free when that vertical lands.
- Existing export lowering (`DateTime.UtcNow` member) is the CLR codegen mapping and is unaffected by the injected seam (which targets the store/preprocess eval path + tests).

### Q5 — Pack-only (stays out of the built-in pack)

Business days, fiscal calendars, time zones, alternate clocks — optional packs on the same seams.

## Grammar-integration ordering (user direction 2026-08-06)

- **Preflight + GI-1…7 + E1 done (2026-08-07):** product structure/annotations on Matcher; expressions in `DslExpressionParser` with **`ExpressionFormRegistry` / `IExpressionPrimaryForm`** for open forms (`Now`, `N days`) without core precedence edits.
- Temporal pack is a **first real consumer** of that expression-form seam (+ analysis specialization registries, absorption §2.5–2.6), the way Sqlite dogfoods annotation facets.
- Grammar re-base is no longer a prereq blocker — **admit `p1-*` when ready** (still explicit roadmap admit).

## Negative tests (documented; author in the `p1-*` suite at admit)

- Unknown unit (`12 fortnights`) **fails closed** at parse/analysis.
- Session **without** temporal pack rejects temporal authoring (opt-out path).
- Type errors: `Date + Date`, `Number + days` (no temporal lhs), unknown unit.
- Fail-loud lowering of unresolved/unregistered specializations (no vacuous pass-through).

## Decision

**Admit `p1-*` when ready** (P3/P2/GI/E1 already done). Suggested backlog: **mcp-minify → mut-safety → p1**. Do not start production temporal work without master-roadmap CURRENT = p1.

## Appendix — doc-only failing-test sketch (not for merge)

```csharp
// Sketch only — never merged as-is. Written to make the product surface testable.
[Test] async Task Now_Minus_12Days_AssignsToDateProperty() {
    // DSL (post-GI):  assign DueDate to Now - 12 days
    // Expect: stored IR = DateOperation(Now, Literal(12), AddDays), lowered = UtcNow.AddDays(12)
}

[Test] async Task ExpiryDate_LessThan_Now_ComparesAgainstInjectedClock() {
    // Policy: IsExpired: policy { ExpiryDate < Now }
    // Expect: preprocessed store form resolves Now via ITemporalClock (fixed in test); true/false as clock says.
}

[Test] async Task UnknownUnit_FailsClosed() {
    // DSL: assign DueDate to Now - 12 fortnights
    // Expect: analysis error, no model change (no vacuous success).
}

[Test] async Task SessionWithoutTemporalPack_RejectsTemporalAuthoring() {
    // Pack-absent domain input set: `Now` / unit forms produce analysis error (opt-out path).
}
```
