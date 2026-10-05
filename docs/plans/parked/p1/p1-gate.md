# p1 — Gate

**Difficulty:** S  
**Status:** `[x]`  
**Prereq:** tasks 0–6 `[x]`  

## Exact steps

1. Full suite green.  
2. Checklist from design lock negatives all covered by tests.  
3. Guide honest.  
4. pr1 pre-ship on dirty tree.  
5. Mark p1-README **DONE** + date; update parent design lock status to “suite complete” if desired.  
6. Do **not** start P9 schedule.

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

- [x] All green  
- [x] Suite Done  

## Status

**Status:** Claimed by p1-gate (fleet agent) 2026-08-13

**Done — gate complete 2026-08-13 (p1-gate fleet agent).**
- Full suite green: build 0 warnings/0 errors; tests 2147/2147 (2145 shipped + 2
  fail-closed tests added during gate for the Number-property date-operand gap).
- Design-lock negatives all covered by tests: unknown unit (fortnights), Date+Date,
  Number+days (literal and property LHS, policy and assign), unresolved specialization
  fail-loud at lowering, pack-absent Now-as-property + authoring rejection.
- Guide honest: temporal authoring/analysis/round-trip shipped; runtime clock eval
  explicitly NOT shipped (`DirectVmAbiEmitter: unsupported node type NamedTypeReference`).
- pr1 pre-ship on phase-3a files: one 🟠 Contract gap found and fixed in-tree (see gate
  notes below); no other 🔴/🟠 in phase-3a scope. Pre-existing drift from other phases
  noted, not fixed.
- P9 (schedule) NOT started, per gate step 6.

## Gate notes

**pr1 findings (phase-3a scope only) by severity:**

- 🔴 Structure: none.
- 🟠 Contract: `TryFoldDateOperation` (DslExpressionParser.cs:99) is syntactic — it
  folds ANY `PropertyAccess` + `N days` into a `DateOperation` before property types
  are known. A **Number property** left operand (`Qty + 3 days`) therefore produced a
  `DateOperation` whose date operand was a Number. When compared against a Date
  (`Qty + 3 days > Expiry`) or assigned to a Date (`assign Expiry to Qty + 3 days`),
  the fold made the operand look like a Date and analysis passed it silently — the
  design-lock negative "Number + days (no temporal lhs) rejected" was only enforced
  for the literal-LHS case (`5 + 3 days`). Fixed: `ExpressionTypeAnalyzer` now routes
  `DateOperation` through a new `CheckDateOperation` that rejects a date operand which
  resolves to a non-Date property (Unknown skips). Failing tests added first:
  `NumberPropertyPlusDays_NoTemporalLeftOperand_Policy_ReportsDateError` and
  `..._AssignRhs_ReportsDateError`. Suite 2145→2147.
- 🟡 Edge case: chained durations (`Now - 12 days - 3 days`) leave a
  `Subtract(DateOperation, Duration)` that passes analysis but fails loud at lowering
  (`NotSupportedException` from bare `Duration`). Not in the shipped single-offset
  claim; fail-loud at the runtime layer is acceptable — noted, not fixed.
- ⚪ Hygiene: none in phase-3a scope.

**Runtime-clock gap: CONFIRMED.** `Now`/`today` lower to `Member(NamedTypeReference("DateTime"), "UtcNow")`, and `DirectVmAbiEmitter.EmitMember` routes the instance through `CompileNode(m.Value)` where `NamedTypeReference` is not in the emitter switch (DirectVmAbiEmitter.cs:285) → `NotSupportedException`. So `simulate_policy`/`evaluate_policy` on `Now` fail as the guide documents. This is the fixed `TimeProvider` seam (T3, design-lock Q4) — **owned by a future suite, not p1-gate; recorded as a documented follow-up, not implemented here.**

**Three-layer defense verification (temporal fail-closed):**
- Unknown unit: parse rejects (`DurationForm` leaves cursor unchanged → FormatException).
- Date+Date: analyze-time (`CheckArithmetic` "not numeric").
- Number+days (literal or property LHS): parse fold + analyze-time rejection
  (`CheckArithmetic` for literal; new `CheckDateOperation` for folded property).
- Unresolved specialization: fail-loud at lowering (`Duration` → NotSupportedException).
- Pack-absent: parse fails for unit/Now authoring; `Now` stays PropertyAccess
  (not lowered as clock); DateOperation print throws at print time.
- Empty/absent sets fail loud — no vacuous success observed.
