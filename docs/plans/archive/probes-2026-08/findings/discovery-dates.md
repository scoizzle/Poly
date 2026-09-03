# Discovery findings — discovery-dates (date/time + defaults)

Slice probes in `probes/discovery-dates/`. Checked with `scripts/run-probe.sh`
(parse → export → Roslyn compile 0/0) + static export review + runtime via MCP
(fresh session per probe).

> **Resolution (2026-08-12):** the date surface is **unauthorized core-DSL surface** —
> added by an agent without approval; the intent is to ship dates **as a pack**. The
> compile-fail bugs below are symptoms of that, so they are **not** being fixed forward.
> Recorded (deferred) in [`docs/plans/dates-to-pack-2026-08-12.md`](../../docs/plans/dates-to-pack-2026-08-12.md).

## F-D1 — `default(guid)` on Text breaks the export (`string ?? Guid`)
- **Signal:** compile-fail (export/runtime divergence)
- **Severity:** 🔴
- **Repro:** `probes/discovery-dates/guid-on-text.poly` — `ExternalId: Text default(guid)`.
  Analysis accepts; export emits `this.ExternalId = externalId ?? Guid.NewGuid();` →
  `error CS0019: Operator '??' cannot be applied to operands of type 'string' and 'Guid'`.
  The runtime stores a GUID **string** (`"b1cc0a3e-…"`), so the export is broken where the
  runtime works.
- **Expected:** `guid` is authorable in `default(...)` (guide line 726); on a `Text`
  property it should produce a GUID string — `Guid.NewGuid().ToString()`.
- **Actual:** the default-expression lowering emits a raw `Guid` value regardless of the
  target property's type → CS0019.
- **Proposed patch:** the default/assign-RHS lowering must produce a value typed for the
  TARGET property (string target → `Guid.NewGuid().ToString()`).

## F-D2 — `now`/`today` Date↔DateTime type-confusion accepted, breaks the export
- **Signal:** compile-fail (export/runtime divergence; guide-drift)
- **Severity:** 🔴
- **Repro:** `probes/discovery-dates/date-now-confusion.poly`:
  - `StartDate: Date default(now)` → `this.StartDate = startDate ?? DateTime.UtcNow;`
    → CS0019 (`DateOnly?` vs `DateTime`).
  - `OpenedAt: DateTime default(today)` → `?? DateOnly.FromDateTime(...)` → CS0019.
  - `assign StartDate to now` → `this.StartDate = DateTime.UtcNow;` → CS0029.
  The analysis rejects `now`/`today` on Number/Text but misses the Date/DateTime
  cross-assignments. The runtime accepts and stores the value.
- **Expected:** guide line 736 claims type-confused defaults are rejected, and line 726
  says `now`/`today` are authorable in assign RHS — so either the analysis must reject
  `now` on `Date` / `today` on `DateTime`, or (per line 726's authorable-claim) the export
  must convert to the target type (`DateOnly.FromDateTime(DateTime.UtcNow)` for `now` on
  `Date`; `DateTime.UtcNow.Date` for `today` on `DateTime`).
- **Actual:** silently accepted → export emits `??`/assignment type mismatches.
- **Proposed patch:** same root as F-D1 — target-typed default/assign-RHS lowering
  (convert `now`/`today`/`guid` to the property's CLR type), or extend the analysis guard
  to reject the cross-type combos.

## F-D3 — the code drifted from the guide: "Date operations — not yet shipped" is correct
- **Signal:** guide-drift (code vs guide; the guide is right)
- **Severity:** 🟠
- **Repro:** `probes/discovery-dates/dates.poly` / `date-edges.poly` compile 0/0 using
  `EndDate + 30`, `EndDate - 30`, `TargetDate - 7 <= ReferenceDate`, `EndDate >= StartDate`,
  `RenewedAt > CreatedAt`, `default(now)`, `default(today)`, `assign X to now`.
- **Expected:** the guide's "Date operations [not yet shipped]" (line 714) matches the
  product intent — dates should be a pack, not core DSL.
- **Actual:** the leaked core surface ships most date operations despite the guide; only
  Date−Date (duration) and Date×Number are rejected.
- **Proposed patch:** none forward — this is the unauthorized-surface scope finding (see
  plan `dates-to-pack-2026-08-12`). When the pack lands, the shipped/not-shipped line moves
  with it.

## F-D4 — `default(guid)` rejection message names non-existent types
- **Signal:** fail-loud-but-sharp (DX)
- **Severity:** 🟡
- **Repro:** `default(guid)` on a `Number` property → "default(guid) is not compatible
  with property type 'Number' (use a Uuid/Guid or Text property)". `Uuid` and `Guid` are
  not authorable DSL types (both produce "Expected property… got 'default'"), and `Text`
  is currently broken by F-D1.
- **Expected:** the hint should point at the single viable path (`Text`), or the surface
  should expose a real Guid-typed property.
- **Actual:** the hint names types that don't exist; the one type that does exist breaks.
- **Proposed patch:** fix the message (and/or F-D1 so `Text` works), then the hint is
  accurate.
