# Round 5 — coordinator baseline triage (findings from existing probes)

Verified 2026-08-12 against round5 baseline (probes/findings/round5/baseline.md).
Each finding spot-verified with `scripts/run-probe.sh` + raw-export inspection + MCP runtime.

## F1 — `default(guid)` on a Text property: export emits `string ?? Guid.NewGuid()` (CS0019)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** runtime keyword defaults
- **Repro:** `probes/discovery-dates/guid-on-text.poly` (`ExternalId: Text default(guid)`);
  also `probes/discovery-b/bookings.poly` line 23 (`ConfirmationCode: Text default(guid)`).
  `scripts/run-probe.sh` both → `error CS0019: Operator '??' cannot be applied to operands of type 'string' and 'Guid'`.
- **Expected:** analysis accepts `default(guid)` on Text (its own diagnostic says "use a Uuid/Guid or **Text** property"); the export must compile — `Guid.NewGuid().ToString()` for Text targets.
- **Actual:** export emits `this.ConfirmationCode = confirmationCode ?? Guid.NewGuid();` → CS0019. Runtime stores a raw `Guid` into the Text slot (cross-typed store).
- **Proposed patch:** in the exporter's runtime-keyword lowering (EffectLoweringPass ~line 590), adapt `guid` to the target CLR type: `Guid`/`Uuid` → `Guid.NewGuid()`, `Text`/`String` → `Guid.NewGuid().ToString()`.

## F2 — `default(now)` / `assign DateProp to now` on a Date property: export emits DateTime for a DateOnly slot (CS0019 / CS0029)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** runtime keyword defaults / assign
- **Repro:** `probes/discovery-dates/date-now-confusion.poly` (`StartDate: Date default(now)` line 14 → CS0019 `DateOnly? ?? DateTime`;
  `assign StartDate to now` line 20 → CS0029 DateTime→DateOnly).
- **Expected:** `now` on a `Date`/DateOnly target adapts to `DateOnly.FromDateTime(DateTime.UtcNow)` (or analysis rejects).
- **Actual:** export emits `this.StartDate = startDate ?? DateTime.UtcNow;` and `this.StartDate = DateTime.UtcNow;` — raw DateTime into DateOnly.
- **Proposed patch:** keyword lowering must adapt to the target property's CLR type (DateOnly vs DateTime), in BOTH default-ctor-param lowering and assign RHS lowering.

## F3 — `default(today)` on a DateTime property: export emits DateOnly for a DateTime slot (CS0019) — keyword adaptation ignores target type
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** runtime keyword defaults
- **Repro:** `probes/discovery-dates/date-now-confusion.poly` (`OpenedAt: DateTime default(today)` line 13 → CS0019 `DateTime? ?? DateOnly`).
- **Expected:** `today` on a DateTime target adapts to `DateTime.Today` (or `DateTime` typed expr); the exported default must compile.
- **Actual:** export emits `this.OpenedAt = openedAt ?? DateOnly.FromDateTime(DateTime.UtcNow);` — DateOnly expr for a DateTime slot.
- **Proposed patch:** same as F2 — keyword→CLR adaptation keyed on the target type. Also note `today` currently lowers to `DateOnly.FromDateTime(DateTime.UtcNow)` (UtcNow, not local `DateTime.Today`) — timezone drift (see F9 in agent notes).
- **Root cause (F1–F3):** analysis buckets `Date`/`DateOnly`/`DateTime` into one `TypeCategory.Date` (ExpressionTypeAnalyzer.CategoryOf) so keyword defaults pass analysis; the exporter and runtime lower `now`/`today`/`guid` to fixed-shape CLR expressions (EffectLoweringPass 578–591, DomainEntityInstance.EvaluateDefaultValue 255–257) without target-type adaptation.

## F4 — non-member enum literal in a `create in` initializer passes analysis → export `CreateBins("Bogus")` (CS1503)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** enum / create-in initializers
- **Repro:** `probes/discovery-agent-b/enum-nonmember.poly` (`create in bins { Status: "Bogus" }` → `error CS1503: Argument 1: cannot convert from 'string' to 'Stock Level'` at export line 49).
- **Expected:** non-member enum string in a create/create-in initializer is rejected at analysis (enum membership is checked for defaults in CheckDefault; the initializer path misses it), or the export adapts it.
- **Actual:** analysis silent; export emits `this.CreateBins("Bogus")` → CS1503. (Round2 F-R2-4 remains open.)
- **Proposed patch:** run CheckEnumMember for enum-typed property initializer literals in `create`/`create in` lowering (same rule as CheckDefault).

## F5 — raw combined export of ANY multi-entity domain does not compile (CS1529); run-probe.sh masks it
- **Signal:** compile-fail (pipeline-masked)
- **Severity:** 🔴
- **Slice:** export pipeline / probe harness
- **Repro:** raw export of any ≥2-entity domain, e.g. `dotnet run --project src/Poly.DslCompiler/Poly.DslCompiler.csproj -c Release -- probes/round5-agent-c/loanbook.poly` (no awk dedup) → `error CS1529: A using clause must precede all other elements...` ×8. `scripts/run-probe.sh` reports 0/0 because its awk dedups `using` lines.
- **Expected:** the combined `_all.cs` stdout export is a product surface (the CLI's default); it must compile standalone — header emitted once at the top.
- **Actual:** the DslCompiler concatenates per-entity files, each generated with its own `#nullable enable` + `using System;` + `using System.Collections.Generic;` header (CSharpGenerator.Generate(IReadOnlyList<TypeDefinitionNode>) emits per-call). Every multi-entity probe since round 1 has been green-washed by the harness dedup; the raw artifact has CS1529.
- **Proposed patch:** in DslCompiler.GenerateAllFiles, emit the header once for the combined output (or strip per-file headers when concatenating).

## F6 — runtime relational comparisons on heap-represented operands compare heap HANDLES: `a < b` always true, `a > b` always false, for DateOnly/DateTime/Guid/string
- **Signal:** export/runtime divergence (+ guide drift)
- **Severity:** 🔴 (top of round — silently wrong policy results)
- **Slice:** runtime VM comparison semantics
- **Repro:** MCP session b1f2a566 (R5DateCmp / R5StrCmp / R5DateBag):
  `evaluate_policy` on `A1 > A2` with A1=2026-01-01, A2=2024-01-01 → **false**; `A1 < A2` → **true**;
  `"zebra" > "b"` → **false**; `"zebra" < "b"` → **true**; `==` works (object.Equals).
  Also `EndDate >= StartDate` (2026 >= 2024) → false on both `properties=` bag and `instanceId=` store paths.
  DSL repro: any policy comparing two Date props or ordering strings — e.g. `probes/discovery-dates/dates.poly` `IsCurrent: policy { EndDate >= StartDate }` (export compiles `this.EndDate >= this.StartDate` — correct C#; runtime returns false).
- **Expected:** `2026-01-01 > 2024-01-01` → true; `"zebra" > "b"` → true (lexicographic); guide says "only comparisons between two date properties are authorable" — they must evaluate correctly.
- **Actual:** DirectVmAbiEmitter.EmitComparison: for relational ops the equality branch (object.Equals for heap values) is skipped; DateOnly/DateTime/Guid/string operands are boxed heap handles (ConvertMemberResult) and the raw handle longs are compared → allocation-order garbage. Left operand allocated first ⇒ `<` always true / `>` always false regardless of values.
- **Proposed patch:** in EmitComparison (and CompileCompareTest / branch paths), for heap-represented operands unbox to CLR objects and compare via IComparable (or value-specific codegen for known value types like DateOnly/DateTime/Guid/string), mirroring the equality path's HeapValueToObject handling.

## Verified-OK on this sweep (not findings)
- Mixed Date/DateTime comparisons now rejected at analysis (round2 F9 closed).
- `pattern` on non-Text and date default mismatches fail loud with clear diagnostics (audit.poly, date-rejects.poly, pattern-nontext.poly now fail closed — stale probes, not bugs).
- `probes/discovery-a/*` fail to parse only because they use the removed `invoke [any|all] Rel.Action` surface (commit 004331da) — stale probes, not product bugs.
