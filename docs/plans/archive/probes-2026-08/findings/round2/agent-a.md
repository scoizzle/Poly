# Discovery round2 — agent-a findings

Slice: DATE/TIME TYPES + DEFAULTS + RUNTIME DEFAULTS

Pipeline: automated path only (MCP disconnected). Every probe run through
`scripts/run-probe.sh` (parse → analyze → export → Roslyn compile-check, 0 errors /
0 warnings gate). Runtime evidence from throwaway TUnit tests (deleted —
`Poly.Tests/DiscoveryAgentB2/*`), style matching `DomainEntityInstanceTests`.

Probes (all under `probes/agent-a/`):
- `loans2.poly` — date arithmetic in actions / entry / exit / if-conditions /
  policies, cross-type Date/DateTime compare + assign, `today` in if-condition
- `entryexit.poly` — `+` / `-` date arithmetic in stage entry/exit effects
- `createin-arith.poly` — date arithmetic in a `create in` initializer
- `createinit-min.poly` — `create Type { defaultedProp }` when the target has another
  runtime-keyword default
- `multiinit.poly`, `multiinit2.poly` — runtime keywords (`now`/`today`/`guid`) as
  non-final values in a `create Type { }` initializer list
- `assign-now.poly` — `assign DateProp to now` vs `assign DateProp to today`
- `audit2.poly` — runtime-keyword defaults on mixed types (Date/DateTime/Text/Number)
- `numdefault.poly` — `default(now/today/guid)` on Number/Text props
- `litdefaults.poly` — non-date literal defaults on Date/DateTime props
- `policynum.poly` — date props compared to number/string literals in policies
- `constr2b.poly` — `default(now)` on Number, date-vs-literal policy, `today`/`now`
  rejected in policies

---

## F1 — Date arithmetic (`+`/`-`) is lowered to `AddDays` ONLY for `assign` targets; policies, `if` conditions, and `create in` initializers emit raw `DateOnly + long` (CS0019); runtime evaluates it as garbage arithmetic

- **Signal:** compile-fail (+ runtime/export divergence)
- **Severity:** 🔴
- **Slice:** date arithmetic in policies / if-conditions / initializers
- **Repro:**
  - `probes/agent-a/loans2.poly` — `IsDueSoon: policy { DueDate - 7 <= ReferenceDate }`
    → `this.DueDate - 7L <= this.ReferenceDate` (CS0019); `IsDueNext: policy { DueDate + 14 > ReferenceDate }` → CS0019
  - `loans2.poly` — `IfArith: if (DueDate + 7 < ReferenceDate)` → `this.DueDate + 7L < this.ReferenceDate` (CS0019)
  - `probes/agent-a/createin-arith.poly` — `create in book { CheckOut: newCheckIn + 14 }` → `newCheckIn + 14L` (CS0019)
  - Runtime (throwaway TUnit): `Runtime_PolicyDateArith_Discriminating` — `DueDate+14 > ReferenceDate` with 08-11/08-30 should be **false** (08-25 > 08-30), runtime returns **true** (garbage `long` arithmetic on boxed DateOnly handles; no throw).
- **Expected:** every authorable surface with the same `DateOnly + 14` intent should lower to `AddDays` (the `assign` path already emits `this.DueDate.AddDays((int)14L)`), and the runtime should either match or fail loud.
- **Actual:** the `AddDays` rewrite lives only in `EffectLoweringPass.Assign` (`EffectLoweringPass.cs:171`); `DomainExpressionLoweringPass` (policies, `if` conditions, create initializer values) never applies it. Export fails CS0019; runtime silently evaluates bogus arithmetic.
- **Proposed patch:** hoist the date-arithmetic rewrite into `DomainExpressionLoweringPass` (type-aware `Add`/`Subtract` → `AddDays(int)`), so policies, `if`, entry/exit, assign, and initializers share one lowering; add a runtime date-arithmetic VM op or fail loud.

## F2 — Date subtraction is NEVER lowered: `assign DueDate to DueDate - 14` and `ReferenceDate - 7` in entry/exit emit CS0019 (export), runtime crashes

- **Signal:** compile-fail (runtime fails loud but sharp/opaque)
- **Severity:** 🔴
- **Slice:** date arithmetic in actions / entry-exit
- **Repro:**
  - `probes/agent-a/loans2.poly` — `RenewShort: assign DueDate to DueDate - 14` → `this.DueDate = this.DueDate - 14L` (CS0019)
  - `probes/agent-a/entryexit.poly` — entry `assign DueDate to ReferenceDate - 7` and exit `assign DueDate to ReferenceDate - 7` → both `this.ReferenceDate - 7L` (CS0019). The `+` twin (`ReferenceDate + 7`) emits `AddDays` and compiles.
  - `probes/discovery-b/bookings.poly` — `CheckOutEarly: action (daysEarly: Number) { assign CheckOut to CheckOut - daysEarly }` → CS0019.
- **Expected:** `DueDate - 14` → `this.DueDate.AddDays((int)(-14))` (or `AddDays(-offset)`), symmetric with the `+` path. The IR already has `DateOperationKind.DiffDays`.
- **Actual:** `EffectLoweringPass.Assign` matches `Ast.Nodes.Add` only (`EffectLoweringPass.cs:172`); no `Subtract` arm. Subtraction in any context → CS0019. Round-1 TUnit observed runtime `IndexOutOfRangeException` from `Heap.UnsafeGet` on this path (throwaway, not re-run here).
- **Proposed patch:** add a `Subtract` arm in `EffectLoweringPass.Assign` and the shared expression pass from F1 (negate offset → `AddDays`).

## F3 — Runtime keywords `now`/`today`/`guid` in `default(...)` are not adapted to the property CLR type — CS0019 for ANY mismatched pair; analysis never rejects; runtime silently stores a wrong-typed value

- **Signal:** compile-fail (+ runtime/export divergence)
- **Severity:** 🔴
- **Slice:** runtime defaults (`default(now/today/guid)`)
- **Repro:** `probes/agent-a/numdefault.poly` — every mismatched pair fails compile:
  - `NumToday: Number default(today)` → `long? ?? DateOnly.FromDateTime(...)` (CS0019)
  - `NumNow: Number default(now)` → `long? ?? DateTime.UtcNow` (CS0019)
  - `NumGuid: Number default(guid)` → `long? ?? Guid.NewGuid()` (CS0019)
  - `TextNow: Text default(now)` → `string ?? DateTime.UtcNow` (CS0019)
  - `TextToday: Text default(today)` → `string ?? DateOnly...` (CS0019)
  - `probes/agent-a/audit2.poly` / `probes/discovery-b/audit.poly` — `Date default(now)`, `DateTime default(today)`, `Date default(guid)`, `Text default(guid)` all CS0019.
  - Runtime (throwaway TUnit): `Runtime_DateDefaultNow_StoresDateTimeInDateProp_Silently`, `Runtime_DateDefaultGuid_StoresGuidInTextProp_Silently`, `Runtime_NumberDefaultToday_StoresDateOnlyInNumberProp_Silently` — the wrong-typed CLR value is stored silently (no throw), and `GetProperty<T>` returns `default` for mismatched `T` reads.
- **Expected:** `default(now)` on a `Date`/`DateOnly` prop should emit `DateOnly.FromDateTime(DateTime.UtcNow)` (the `today` form); `default(guid)` on `Text` should emit `Guid.NewGuid().ToString()`; or analysis rejects the mismatch fail-closed. Only the matching pairs (`DateTime default(now)`, `Date default(today)`) currently compile.
- **Actual:** `EffectLoweringPass.LowerDefaultExpression` returns a fixed CLR expression per keyword with no property-type adaptation; analysis has no default/type mismatch diagnostic. Export fails at compile; runtime diverges silently (stores `DateTime` in a `Date` prop, `Guid` in a `Text` prop, `DateOnly` in a `Number` prop).
- **Proposed patch:** make `LowerDefaultExpression`/`EvaluateDefaultValue` type-aware (pass the property type; `now` on DateOnly → `DateOnly.FromDateTime`; `guid` on string → `Guid.NewGuid().ToString()`; reject otherwise at analysis).

## F4 — Non-date literal defaults on date props (`Date default("2024-01-01")`, `Date default(0)`) emit CS1750; no date-literal syntax, no analysis rejection

- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** literal `default(value)` on date-typed properties
- **Repro:** `probes/agent-a/litdefaults.poly`:
  - `BadDateLiteral: Date default("2024-01-01")` → `DateOnly badDateLiteral = "2024-01-01"` (CS1750)
  - `BadDateNumber: Date default(0)` → `DateOnly badDateNumber = 0L` (CS1750)
  - `BadDateTimeLiteral: DateTime default("2024-01-01")` → `DateTime = "..."` (CS1750)
  - Also in `probes/discovery-b/audit.poly` (round-1 probe, still failing).
- **Expected:** either a date-literal form (`default(2024-01-01)`/ISO string → `DateOnly.Parse(...)`) or a fail-closed analysis rejection naming the mismatch.
- **Actual:** `LowerDefaultConstantNode` passes the raw `Literal` through as the C# optional-param default with no type mapping; the DSL grammar has no date-literal token; analysis accepts the constraint. Compile is the only failure point.
- **Proposed patch:** validate default-literal vs property type at analysis; map ISO date strings to `DateOnly.Parse(...)` (or reject).

## F5 — `assign DateProp to now` → CS0029 (DateTime→DateOnly) while `assign DateProp to today` compiles: `now` is not adapted to Date targets anywhere (default, assign)

- **Signal:** compile-fail (+ runtime divergence)
- **Severity:** 🔴
- **Slice:** runtime keywords in assign / defaults on `Date` props
- **Repro:** `probes/agent-a/assign-now.poly`:
  - `entry { assign CheckIn to now }` → `this.CheckIn = DateTime.UtcNow;` (CS0029)
  - `Confirm: action { assign CheckIn to now }` / `assign CheckOut to now` → CS0029 ×2
  - `assign CheckedOutAt to now` (DateTime target) compiles and runs correctly.
  - Runtime (throwaway TUnit): `F5_ActionAssignToday_OnDateProp_StoresWhat` — action `assign CheckIn to today` stores the **whole property-values dictionary** (garbage); `F5_EntryAssignToday_OnDateProp_StoresWhat` — entry `assign CheckIn to today` stores **null**. Neither stores a `DateOnly`. (Round-1's baseline "assign `today` works" verified only the **export**; the runtime assign path never resolves these keywords.)
- **Expected:** `assign DateProp to now` should behave like `assign DateProp to today` (`DateOnly.FromDateTime(DateTime.UtcNow)`), or analysis should reject `now` on a Date target.
- **Actual:** export emits raw `DateTime.UtcNow` for a DateOnly target (CS0029); runtime doesn't resolve the keyword in the action/entry assign path (`DomainExpressionLoweringPass` only handles `now/today/guid` when `_useThisReference` is set — `DomainExpressionLoweringPass.cs:73`) and silently stores a Dictionary (action) or null (entry). `today` is adapted in the export but also broken at runtime in action/entry assigns.
- **Proposed patch:** adapt `now` (and `today`/`guid`) to the assign/initializer TARGET type in the shared expression lowering, and make the runtime assign path resolve these keywords (or fail loud).

## F6 — Policies cannot compare dates to `now`/`today` (analysis rejects), yet `today` WORKS in `if` conditions and `now`/`today` work in defaults/assigns — undocumented, inconsistent surface

- **Signal:** guide-drift / fail-loud-but-sharp
- **Severity:** 🟠
- **Slice:** date comparisons in policies / conditions
- **Repro:**
  - `probes/agent-a/constr2b.poly` — `PolicyCompareToday: policy { CheckIn < today }` and `PolicyCompareNow: policy { CheckIn <= now }` → `Policy references property 'today'/'now' which does not exist on entity ...` (analysis).
  - Contrast: `loans2.poly` — `IfCompareToday: action { if (DueDate < today) ... }` **compiles** and lowers to `this.DueDate < DateOnly.FromDateTime(DateTime.UtcNow)` (export line verified).
- **Expected:** the guide (§8) lists "Date operations" as not shipped and never documents `now/today/guid` at all; a slice intent of `DueDate < today` should either be authorable everywhere or rejected everywhere with a clear diagnostic.
- **Actual:** policies reject `today`/`now` at analysis, while `if` conditions (same expression grammar, "preprocess like policy eval" per guide) accept `today`. The split is undocumented; an author porting a policy to an `if` gets different behavior.
- **Proposed patch:** either allow `now`/`today` in policy bodies (resolve as runtime constants like the `if`/assign path) or reject in `if` too and document the restriction with a targeted message.

## F7 — `create Type { boundDefaultedProp }` on a target that has a runtime-keyword default crashes codegen with a misleading error ("'today' is not a member of an enum")

- **Signal:** compile-fail (fail-loud but wrong reason / dead-ends)
- **Severity:** 🟡
- **Slice:** runtime defaults interacting with `create`/`create in` initializer binding
- **Repro:** `probes/agent-a/createinit-min.poly`:
  ```
  AuditEvent: entity {
    RecordedOn: Date default(today)
    Count: Number default(0)
    Log: action { create AuditEvent { Count: 5 } }
  }
  ```
  → `Code generation failed: default(today) on property 'RecordedOn' (type 'Date') cannot be lowered: 'today' is not a member of an enum that 'RecordedOn' is typed with.`
- **Expected:** binding `Count: 5` in a create should leave the unbound `RecordedOn` default to its (valid, shippable) `default(today)`; the create should compile like the plain-entity case does.
- **Actual:** `AppendDefaultedPropArgs` (`EffectLoweringPass.cs:584-595`) evaluates `LowerDefaultConstantNode` for EVERY defaulted prop when ANY defaulted prop is bound; a runtime-keyword default (`now`/`today`/`guid`) throws `NotSupportedException` there — even though `LowerDefaultExpression` (the runtime-keyword path) is available and would return a sentinel. Same crash for `default(now)`/`default(guid)`. Error message points at "enum", not the real cause.
- **Proposed patch:** in `AppendDefaultedPropArgs`, prefer `LowerDefaultExpression` first and only fall through to `LowerDefaultConstantNode` when it returns null (mirror the `Create`-factory path at exporter line 769-777 which already handles this).

## F8 — Runtime keywords (`now`/`today`/`guid`) as non-final values in a `create Type { }` initializer list are parsed as path-prefix navs → parse error "Expected property name, got ':'"

- **Signal:** compile-fail (parse)
- **Severity:** 🟡
- **Slice:** runtime keywords in create initializers
- **Repro:**
  - `probes/agent-a/multiinit.poly` — `create AuditEvent { OccurredAt: now Label: "x" }` → `Parse error: Expected property name, got ':' (line 8, col 47)`
  - `probes/agent-a/multiinit2.poly` — `create AuditEvent { RecordedOn: today ReferenceId: guid }` → `Parse error: ... (line 8, col 55)`
  - A single-initializer create (`create AuditEvent { OccurredAt: now }`) parses, exports, and compiles 0/0 — so the value is legal; only the list form breaks.
- **Expected:** `now`/`today`/`guid` should be valid initializer values anywhere in the list (they're accepted as single values and as defaults).
- **Actual:** the expression parser sees `now Label`/`today ReferenceId` as a path-prefix navigation (`RelationshipNavigation`), so the following `:` fails. `assign X to now` and `if (...today)` don't hit this because they don't sit next to a bare identifier in a list.
- **Proposed patch:** reserve `now`/`today`/`guid` as keyword tokens in the expression lexer/parser (they are already special-cased in lowering), so a create-initializer list parses them as scalar values, not nav roots.

## F9 — Cross-type Date/DateTime compares and assigns: `DateTime < Date` in policies (CS0019) and `assign DateTimeProp to DateProp` (CS0029) pass analysis and only fail at compile; no diagnostic

- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** mixed Date/DateTime property typing
- **Repro:** `probes/agent-a/loans2.poly`:
  - `CrossTypeCompare: policy { CheckedOutAt < DueDate }` → `this.CheckedOutAt < this.DueDate` (CS0019; DateTime vs DateOnly)
  - `CrossTypeAssign: action { assign CheckedOutAt to DueDate }` → `this.CheckedOutAt = this.DueDate;` (CS0029)
  - `constr2b.poly` — `PolicyLiteralDate: policy { CheckIn > "2024-01-01" }` and `policynum.poly` — `PolicyDateNum: policy { CheckIn > 5 }` → `DateOnly > string` / `DateOnly > long` (CS0019).
- **Expected:** analysis should reject cross-type date comparisons / assigns and date-vs-literal compares fail-closed (a `Date` prop compared to a string or number is never valid), so the author gets a domain-level diagnostic instead of a C# compiler error.
- **Actual:** no analysis check for date/CLR-type compatibility; the first failure point is the Roslyn compile-check. Runtime (throwaway `Runtime_CrossTypeDatePolicy_ComparesWrongly`) silently evaluates the mixed compare against boxed handles.
- **Proposed patch:** add an analysis diagnostic (type-compatibility) covering date-vs-date-with-different-CLR-type, date-vs-string-literal, and date-vs-number compares; and reject cross-type date assigns (or auto-convert DateTime→Date/Date→DateTime at the boundary with a documented rule).

---

## Round-1 cross-checks (explicit)

- **F1 / F2 / F3 / F4 / F5 (round-1): all still reproduce** via the probes above and the round-1 probes (`probes/discovery-b/{loans,bookings,audit}.poly` still fail the compile gate: loans 3 errors, bookings 2, audit 8).
- **F6 (round-1: enum-member default on non-enum prop) — FIXED, no longer reproduces.** `probes/agent-a/enumondate.poly` (`DueDate: Date default(Draft)`) now fails loud at codegen (`default(Draft) ... is not a member of an enum that 'DueDate' is typed with`), consistent with round-1's "now fails loud". Not re-reported as a new finding.
- Round-1's `enumfix2.poly`-style correctly-typed enum defaults (`Status: Status default(Draft)` on an enum-typed prop) still compile and work.
