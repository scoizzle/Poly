# Discovery-b findings — date/time types, defaults, runtime defaults

Slice: DATE/TIME TYPES + DEFAULTS + RUNTIME DEFAULTS (Date/DateTime property types,
`default(value)` incl. `now`/`today`/`guid` and enum-member defaults, date arithmetic
in actions / entry-exit / policies, policies comparing dates).

Pipeline: automated path only (MCP disconnected). Each probe run through
`scripts/run-probe.sh probes/discovery-b/<name>.poly` (parse → analyze → export →
Roslyn compile-check, 0 errors/0 warnings required). Runtime evidence from a
throwaway TUnit probe (deleted — `Poly.Tests/Mcp/ZzDiscoveryBProbeTests.cs`).

Probes:
- `probes/discovery-b/loans.poly`
- `probes/discovery-b/bookings.poly`
- `probes/discovery-b/audit.poly`

---

## F1 — Date arithmetic is lowered to AddDays only for `assign` targets; policies and `create in` initializers emit raw `DateOnly + long` (CS0019)

- **Signal:** compile-fail (+ runtime/export divergence)
- **Severity:** 🔴
- **Slice:** date arithmetic in actions / entry-exit / policies
- **Repro:**
  - `probes/discovery-b/loans.poly` — `IsDueSoon: policy { DueDate - 7 <= ReferenceDate }` and
    `IsDueNext: policy { DueDate + 14 > ReferenceDate }`
  - `probes/discovery-b/bookings.poly` — `Extend` compiles, but a `create in bookings { CheckOut: newCheckIn + 14 }` initializer (see `/tmp/createin.poly`) fails
- **Expected:** `DueDate.AddDays(14)` / `DueDate.AddDays((int)7)` wherever the same DSL authoring intent appears — the assign path already does this
  (`Renew: assign DueDate to DueDate + 14` → `this.DueDate.AddDays((int)14L)`).
- **Actual:** policies lower to `this.DueDate + 14L > this.ReferenceDate` and `this.DueDate - 7L <= this.ReferenceDate` (CS0019); `create in` initializers lower to `newCheckIn + 14L` (CS0019). The AddDays rewrite lives only in `EffectLoweringPass.Assign`; `DomainExpressionLoweringPass` (policy bodies, `if` conditions, initializer values) never sees it. Runtime evaluates the policy *silently* (VM does `long` arithmetic on boxed heap handles — no throw), so export fails loud while runtime returns a garbage boolean.
- **Proposed patch:** hoist the date-arithmetic rewrite into `DomainExpressionLoweringPass` (emit `Invoke(AddDays, [cast int])` when a `+`/`-` operand is a date-typed member) and add `Subtract` (→ `AddDays` with negated offset, or `DateOnly.AddDays(int)`) so assign, policy, `if`, and initializer paths share one lowering.

## F2 — Date subtraction in `assign` (`DueDate - 14`) is never lowered — export CS0019, runtime crashes

- **Signal:** compile-fail (runtime fails loud but sharp/opaque)
- **Severity:** 🔴
- **Slice:** date arithmetic in actions
- **Repro:**
  - `probes/discovery-b/loans.poly` — `RenewShort: action { assign DueDate to DueDate - 14 }`
  - `probes/discovery-b/bookings.poly` — `CheckOutEarly: action (daysEarly: Number) { assign CheckOut to CheckOut - daysEarly }`
- **Expected:** `assign DueDate to DueDate - 14` → `this.DueDate.AddDays(-14)` (or `AddDays((int)(-14))`), symmetric with the `+` path. The IR already has `DateOperationKind.DiffDays`.
- **Actual:** export emits `this.DueDate - 14L` (CS0019). The `EffectLoweringPass.Assign` rewrite matches `Ast.Nodes.Add` only — no `Subtract` arm. Runtime (throwaway TUnit `DateSubtract_InAction_RuntimeSilentlyWrong`) throws `IndexOutOfRangeException` from `Heap.UnsafeGet` — an opaque crash, not a clean fail.
- **Proposed patch:** add a `Subtract` arm in `EffectLoweringPass.Assign` (mirror of the `Add` arm, negate or emit `AddDays(-offset)`), and the same in `DomainExpressionLoweringPass` per F1.

## F3 — Runtime defaults (`default(now/today/guid)`) don't adapt to the property CLR type; mismatches are caught only at C# compile (never at analysis); runtime stores the wrong-typed value silently

- **Signal:** compile-fail (+ runtime/export divergence)
- **Severity:** 🔴
- **Slice:** runtime defaults (`now`/`today`/`guid`) in `default(value)`
- **Repro:** `probes/discovery-b/audit.poly` (all four below fail compile):
  - `BadDateNow: Date default(now)` → `DateOnly? badDateNow = null; this.BadDateNow = badDateNow ?? DateTime.UtcNow;` → **CS0019** (`DateOnly?` vs `DateTime`)
  - `BadDateTimeToday: DateTime default(today)` → `DateTime? ?? DateOnly.FromDateTime(DateTime.UtcNow)` → **CS0019**
  - `BadGuidOnDate: Date default(guid)` → `DateOnly? ?? Guid.NewGuid()` → **CS0019**
  - `ReferenceId: Text default(guid)` → `string? referenceId ?? Guid.NewGuid()` → **CS0019**
- **Expected:** `default(now)` on a `Date` prop should produce `DateOnly.FromDateTime(DateTime.UtcNow)` (the `today` form), or analysis must reject the mismatch (fail closed). The matching pairs (`OccurredAt: DateTime default(now)`, `RecordedOn: Date default(today)`) compile and run correctly.
- **Actual:** `EffectLoweringPass.LowerDefaultExpression` returns a fixed CLR expression per keyword with no `typeHint`/property-type adaptation; analysis has no diagnostic for default/property type mismatch. Runtime `DomainEntityInstance.EvaluateDefaultValue` stores the raw `DateTime` (boxed) in the `Date`-typed property with no error (TUnit: `GetProperty<object>("BadDateNow")` is `DateTime`), so export fails loud while runtime silently holds a value of the wrong CLR type — divergence.
- **Proposed patch:** make `LowerDefaultExpression`/`EvaluateDefaultValue` type-aware (`now` on `Date`/`DateOnly` → `DateOnly.FromDateTime(DateTime.UtcNow)`; `guid` on `Text` → `Guid.NewGuid().ToString()` or analysis reject), and add an analysis diagnostic for default-expression/type mismatches.

## F4 — Non-date default literals on date properties (`Date default("2024-01-01")`, `Date default(0)`) emit invalid C# defaults (CS1750); no date literal support, no analysis rejection

- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** literal `default(value)` on date-typed properties
- **Repro:** `probes/discovery-b/audit.poly`:
  - `BadDateLiteral: Date default("2024-01-01")` → `private AuditEvent(DateOnly badDateLiteral = "2024-01-01", …)` → **CS1750**
  - `BadDateNumber: Date default(0)` → `DateOnly badDateNumber = 0L, …` → **CS1750**
- **Expected:** either a date-literal parser form (`default(2024-01-01)` / ISO string) or a fail-closed analysis rejection. 
- **Actual:** `LowerDefaultConstantNode` passes the raw `Literal` straight through as the C# optional-parameter default with no type mapping; the DSL grammar has no date literal token, and analysis accepts the constraint. Compile is the only place it fails.
- **Proposed patch:** validate default-literal type vs property type at analysis; map date literal strings to `DateOnly.Parse(...)` constants (or reject).

## F5 — Policies cannot compare dates to `now`/`today` (analysis rejects as unknown property), while `now`/`today` work in `default` and `assign` RHS — undocumented split surface

- **Signal:** guide-drift / fail-loud-but-sharp
- **Severity:** 🟠
- **Slice:** policies comparing dates
- **Repro:** `probes/discovery-b/loans.poly` `IsOverdue: policy { DueDate < today }` and `bookings.poly` `BookingIsCurrent: policy { BookedAt <= now }` → `Compilation failed: Policy references property 'today'/'now' which does not exist on entity …` (analysis `PolicyConstraintAnalyzer`).
- **Expected:** slice intent — date-comparing policies such as `DueDate < today` — should be authorable (the export/runtime both have a `now`/`today` lowering), or the guide must state policies can't use relative dates.
- **Actual:** rejected at analysis. `now`/`today`/`guid` are supported (and shipped) in `default(...)` (runtime + export) and in `assign` RHS (`assign ReturnedAt to now` → `DateTime.UtcNow`), but not in policy bodies. With no date literals either, the only date-comparing policy is between two real `Date` props (`DueDate < ReferenceDate` — compiles and evaluates correctly at runtime). Guide §8 lists "Date operations" as not shipped and never documents `now`/`today`/`guid` — the guide understates the shipped default/assign surface and can't guide a policy author.
- **Proposed patch:** either allow `now`/`today` in policy bodies (treat as runtime-constant PropertyAccess like assign/initializer paths) or document the restriction and add a targeted diagnostic message ("date policies must compare two date properties; `today`/`now` unsupported in policies").

## F6 — enum-member `default(MemberName)` on a non-enum (date) property: silently dropped in the export, applied as a string at runtime — divergence + silent gap

- **Signal:** silent-gap (+ runtime/export divergence)
- **Severity:** 🟠
- **Slice:** enum-member defaults (`default(value)` incl. enum-member defaults)
- **Repro:** `/tmp/enumdefault.poly` (also reproducible in any domain): `DueDate: Date default(Draft)` on `Task` with `Status: enum { Draft, Open }`:
  - Export compiles 0 errors/0 warnings but emits `Create(DateOnly dueDate, …)` and `private Task(DateOnly dueDate, …)` — the `default(Draft)` is **silently gone**; `DueDate` becomes a *required* ctor param.
  - Runtime (TUnit `DefaultEnumMember_OnDate_RuntimeStoresString`): `DomainEntityInstance.Create` stores `"Draft"` (string) in the `Date` property.
- **Expected:** analysis reject (member not a member of any enum the property is typed with, or property not enum-typed) — fail closed; or a clear warning.
- **Actual:** `LowerDefaultConstantNode` returns null for non-literal, non-enum-typed PropertyAccess; `DefaultValue: null` renders as no default at all (silent signature change); runtime applies the member-name string. A DSL default that changes the Create signature with zero diagnostics.
- **Proposed patch:** in the exporter, when `LowerDefaultConstantNode` returns null for a default that isn't a runtime keyword, fail the export (throw) or emit a `default!` + analysis diagnostic rather than silently dropping the constraint.

## F7 — The date/time type names the exporter/lowering know (Timestamp, DateOnly, TimeOnly, TimeSpan, Duration, Guid, Uuid, Time) are not authorable — parser accepts only Text/Number/Boolean/DateTime/Date

- **Signal:** fail-loud-but-sharp (surface gap with misleading error)
- **Severity:** 🟡
- **Slice:** date/time type surface (slice intent lists DateTime/Timestamp/Date/DateOnly/Time/TimeOnly/Duration/TimeSpan/Uuid/Guid properties)
- **Repro:** `/tmp/types.poly`: `Thing: entity { A: Time }` (and each of `Duration/Uuid/Guid/Timestamp/DateOnly/TimeOnly/TimeSpan`) →
  `Parse error: Navigation property 'A' references unknown entity 'Time'. No entity with that name was found in the domain.`
- **Expected:** these type names either author as the mapped CLR primitives (`Time→TimeOnly`, `Uuid→Guid`, …) or produce a clear "primitive type 'Time' is not supported" parse error.
- **Actual:** the token reader/parser (`DslTokenReader.WordToKind`, `PolyDslParser.ParseProperty`/`ParseTypeName`) recognizes only `Text/Number/Boolean/DateTime/Date`; the remaining names fall through to the nav-property path and report "references unknown entity 'Time'" — misleading (the user wrote a primitive type, not a navigation). `DomainToCSharpExporter.MapDomainTypeRef`, `DefaultForDomainType`, and `IsDateTimeDomainType` all carry unreachable arms for these names. Only `Date` and `DateTime` properties are actually shippable; the guide documents none of the date types.
- **Proposed patch:** extend the token map + `ParseProperty`/`ParseTypeName` to the exporter's alias set (`Timestamp`, `Time`, `Duration`, `Uuid`, …) and `IsPrimitiveTypeToken` guard, or narrow the exporter to the authorable set; improve the unknown-entity error to detect "primitive-like" capitalized type names.

---

## Baseline (worked — for contrast)

- `Renew: assign DueDate to DueDate + 14` → `this.DueDate.AddDays((int)14L)` compiles and matches runtime (TUnit `DateAdd_InAction_RuntimeMatchesExport`: 2026-08-11 → 2026-08-25).
- `default(now)` on `DateTime` and `default(today)` on `Date` compile; entry `assign CheckedOutAt to now` runs in the ctor; `assign ReturnedAt to now` in exit effects lower to `DateTime.UtcNow`.
- Policy `DueDate < ReferenceDate` compiles and evaluates correctly at runtime (TUnit `PolicyDateCompare_RealProperties_RuntimeWorks`).
- Enum-member defaults on the correctly-typed enum prop (`Status: LoanStatus default(Active)`) work in both paths.
