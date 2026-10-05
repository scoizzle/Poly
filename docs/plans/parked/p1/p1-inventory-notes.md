# p1 temporal IR — inventory notes

**Task:** [`p1-0-inventory-ir.md`](./p1-0-inventory-ir.md)  
**Date:** 2026-08-13  
Scope: inventory existing temporal IR (design-lock paths) + gaps for tasks 1–4. **No production code was changed** (docs only).

---

## A. Inventory table

| Asset | File | Exists? | Notes |
|-------|------|---------|-------|
| `DateOperation` / `DateOperationKind` | `DomainExpression.cs` | ✅ | `DateOperationKind { AddDays, AddMonths, DiffDays }` enum (DomainExpression.cs:177); `DateOperation(Date, Offset, Kind)` record (:179–185); factory `DomainExpression.DateOp(date, offset, kind)` (:36). Dispatch routed in `DomainExpressionDispatch.cs` (:27 in Route, :53 handler) — new subtype = compile error. Rewrite recurses Date + Offset (`DomainExpressionRewriteBase.cs`:47). |
| Lowering AddDays/Months | `DomainExpressionLoweringPass.cs` | ✅ | `DateOperation` override (:274–286): AddDays → `Invoke(Member(date, "AddDays"), offset)`, AddMonths → `Invoke(Member(date, "AddMonths"), offset)`, DiffDays → `Invoke(Member(date, "Subtract"), offset)`; unknown kind → `NotSupportedException` (fail-loud). Tests: `DateOperation_AddDays/Months/DiffDays_LowersToInvoke` (`DomainExpressionLoweringPassTests`), `DomainExpression_DateOperation_CanBeStoredInAssignEffect` (`DomainEvolutionApplicatorTests`), `DateOperation_LowersWithoutThrowing` (`DomainExpressionVmExecutionTests` — VM `Invoke`/heap path is a documented gap, not a blocker for p1). |
| `default now` strings | `EffectLoweringPass` | ✅ (limited) | `LowerDefaultExpression` (EffectLoweringPass.cs:600): `now`/`utcnow`/`today`/`guid` handled as **`PropertyAccess` name keywords** → `DateTime.UtcNow` / `DateOnly.FromDateTime(UtcNow)` / `DateTime.Today` / `Guid.NewGuid()` with target-type adaptation (`NamedTypeReference` hint). Used for `DefaultValueConstraint` string forms and assign-RHS keywords (:166–172). Same keyword semantics in `ExpressionTypeAnalyzer.cs` (:449, :494–495) and runtime `DomainEntityInstance.cs` (:257–262, :700). **Not** a `DomainExpression` node; not a clock seam. |
| `Now` as DomainExpression node | — | ❌ | No `Now` / `Today` / `ClockNow` expression node exists in the IR. Grep: no `record Now`, `ClockNow`, or `TimeProvider` in core `Poly/**` — only design docs reference `System.TimeProvider` (BCL, net10.0). Policy/expression IR has no clock node; authoring surface has no `Now` token (fluent-IR-only `DateOperation`). |
| ExpressionFormRegistry | `Parsing/` | ✅ (E1 bridge) | `Poly/DomainModeling/Parsing/ExpressionFormRegistry.cs` — E1 open-form seam: `IExpressionPrimaryForm` (cited-gap RD escape only, pack-host lock 13), `RegisterGrammarContributor` (Grammar patterns on existing rules), `RegisterPrintBinder` (`IExpressionPrintBinder` → `ExpressionPrintRegistry`). **No product forms yet** — only test forms (`MagicLiteralForm` / `DurationForm` in `DslExpressionE1Tests`, `MagicLiteralForm` in `DslExpressionFragmentTests`). E1 tests prove: `N unit` pattern registers on **both** `expr-primary` and `expr-primary-no-not` (:103–127), folds via the RD escape, prints via binder, round-trips; without the pattern, identifier falls back to `PropertyAccess` (`WithoutPattern_MagicIsPropertyAccess` :191–206). Registration surfaces: `DomainInputBuilder.Create().ExpressionForms` (DomainInputSet.cs:101) + `RegisterExpressionForm` (:134); `IDomainPack`/`AddPack`/`PackContext.ExpressionForms` already exist (Packs/, pack-host wave 2) — no pack-1-3-style IDomainPack work is needed by p1-3. |

**Related (context, not in table):** `DomainToCSharpExporter.cs` (:192, :836) calls `LowerDefaultExpression` for export; `DomainEntityInstance` resolves `now`/`today`/`guid` at runtime via `DateTime.UtcNow`/`DateTime.Today` (not injectable — the T3 clock seam is p1 work). Date arithmetic on generic `Add`/`Subtract` (`DueDate + 14` → `AddDays`) is lowered by the expression pass (EffectLoweringPass.cs:193–194).

---

## B. Gaps for tasks 1–4 (not TZ/schedule)

| Task | Gap |
|------|-----|
| **1 — `Now`/`today` node + form** | No clock `DomainExpression` node. Add node (name for what it is), wire `DomainExpressionDispatch.Route` (compile-enforced), `DomainExpressionRewriteBase`, and lowering. Clock host: `System.TimeProvider` is BCL (net10.0) and unused in core yet — T3 lock says injectable; current clock sites hardcode `DateTime.UtcNow`. Implement `IExpressionPrimaryForm` for exact identifier `Now` (and `today` if lock requires), leaving the cursor unchanged on non-match; without the form, identifier stays `PropertyAccess` (E1 proves the fallback). |
| **2 — Duration forms** | No `days`/`months` tokens or unit grammar in core (lock T1: no core TokenKind per unit). `DateOperation` is fluent-IR-only — **not product-DSL-authorable** today. Need `N unit` primary form → duration IR → binary `Now - 12 days` / `DueDate + 14 days` resolves to `DateOperation` (design-lock Q1/Q2). E1 infra already hosts `Number Identifier` on both primaries; decide smallest path (keep arithmetic nodes + analysis resolve vs fold-time) and document. `12 fortnights` must **not** produce a successful `DateOperation` (parse or analysis reject). |
| **3 — Pack registration** | No built-in temporal pack. Seam exists (`IDomainPack`/`DomainInputBuilder.AddPack`, `PackContext.ExpressionForms`, duplicate-id fail-closed) — task is to write `TemporalPack` (or `CreateWithTemporalPack`) registering the task-1/2 forms + grammar contributors + print binders, and wire product default input set (design lock: built-in pack product default; sessions without it must reject temporal authoring). Watch: default-on could clash with existing tests that use `now` as a property name. |
| **4 — Analysis fail-closed** | No analysis-time rejection for unknown unit, `Date + Date`, `Number + days` (no temporal lhs), or unresolved specialization. Only current fail-loud point is lower-time `NotSupportedException` on unknown `DateOperationKind` (DomainExpressionLoweringPass.cs:285). `ExpressionTypeAnalyzer` already special-cases keyword `now`/`utcnow`/`today`/`guid` names (:449, :494) — a real `Now` node needs a Date/DateTime type-category rule. Pack-absent: `Now` must not lower as clock (PropertyAccess or error — task 1 picks, task 4 verifies). |

---

## C. Verification

- [x] Notes file complete
- [x] No production code changes (edit scope = `p1-inventory-notes.md` only)
