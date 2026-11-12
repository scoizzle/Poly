# Domain type model and numeric rules (Q5, Q6, OQ-3, OQ-6, OQ-11)

Status: **Transcription of owner rulings Q5, Q6, OQ-3, OQ-6, OQ-11; this file adds no new decision.**

Recorded by slice T-01 of theme T ([#679](https://github.com/scoizzle/Poly/issues/679), epic
[#677](https://github.com/scoizzle/Poly/issues/677)) so the slices that implement the model are held to the
rulings that bind them. The rulings are the owner's rows on epic
[#659](https://github.com/scoizzle/Poly/issues/659), quoted verbatim below; prose between quotes is this
file's framing only. The consumer contract `NumericRules` is declared in #677 for P-05; this file is its
provider and adds no API of its own. A clause a ruling does not settle is tagged *Open* and names the slice
that decides it, rather than being filled in here.

## Rulings, quoted

### Q5 — type model, one gate

> Do NOT fold VM analysis into define/check; instead replace `ExpressionTypeAnalyzer`'s coarse `TypeCategory`
> model with the Poly/Introspection type/member model so domain analysis is the one gate; check and simulate
> share one session.

— Q5 row (gate, D), [#659 comment 6801963063](https://github.com/scoizzle/Poly/issues/659#issuecomment-6801963063).

### Q6 — numeric

> New `checked` region (like C#): overflow inside returns an arithmetic failure, outside it wraps; Number /
> Number is integer division (+ warning when assigned to Decimal); Decimal -> Number rounding only when
> analysis proves it safe, else an error.

— Q6 row (D, O), [#659 comment 6801963063](https://github.com/scoizzle/Poly/issues/659#issuecomment-6801963063).

The `checked`-region half is owned by
[`2026-11-11-checked-arithmetic-region.md`](2026-11-11-checked-arithmetic-region.md) (F-06/F-07); this file
owns the typing half and does not restate it.

### OQ-3 — Decimal -> Number rounding mode (T-12)

> `MidpointRounding.AwayFromZero`, emitted explicitly in BOTH the simulator and the generated C#. Correction
> recorded: no `RoundToNumber` symbol exists today (the earlier "keep today's RoundToNumber" claim was
> wrong); current behaviour differs between the VM coercion (`Convert.ToInt64`, to-even) and a `(long)` cast
> (truncation).

— OQ-3 row, [#659 comment 6805267269](https://github.com/scoizzle/Poly/issues/659#issuecomment-6805267269).

### OQ-6 — ExactNumber representation (T-02/V-05)

> `ExactNumber` is decimal-backed and saturates to unbounded on overflow (never throws, never wraps).

— OQ-6 row, [#659 comment 6805267269](https://github.com/scoizzle/Poly/issues/659#issuecomment-6805267269).

**OQ-6's "never throws, never wraps" is about `ExactNumber` — analysis-time bound arithmetic — not runtime
`Number`, which wraps outside `checked` per Q6.**

### OQ-11 — operator lookup (T)

> `OperatorTable.cs` maps syntax -> operator kind ONLY. Operand typing and result resolution go through a new
> Introspection `GetOperator` lookup owned by T. Built-in numeric/temporal operators are defined on the
> built-in types; value-type overloads are value-type members.

— OQ-11 row, [#659 comment 6805267269](https://github.com/scoizzle/Poly/issues/659#issuecomment-6805267269).

Planned (OQ-11): an Introspection `GetOperator` lookup owns operand typing and result resolution;
`Meaning/OperatorTable.cs` (planned, T-03) will map syntax → operator kind only.

## Rules

Legend. **ruling clause** — copied from a quote above, never paraphrased. **#679 plan direction** — the
owner-approved plan, not a ruling. **Open** — not settled by either; the named slice decides it.

| # | Rule | Provenance |
|---|---|---|
| R0 | define/check acceptance is the one gate: what it accepts, simulate, printed C# and polyc accept. | **ruling clause** — Q5: "domain analysis is the one gate". The harness is T-05 `GateParity`, closed by T-13 |
| R1 | `Unresolved` is never compatible — not a wildcard. | **#679 plan direction** (goal: "`Unknown` never compatible"). Q5 says replace the model; it does not say this |
| R2 | `Decimal` ≠ `Number`; `Number` ≡ `Int64`. | **#679 plan direction** — "Decimal distinct from Number" in the goal, and "Number ≡ Int64" in T-03's red test; the existing mapping (below) already spells `Number` as `long`/`bigint` |
| R3 | optionality is a flag beside the definition, not a wrapped type; `null` only into an optional target. | **#679 plan direction** (goal: "first-class optionality") |
| R4 | `Number / Number` is integer division; assigning it to `Decimal` warns. | **ruling clause** — Q6: "Number / Number is integer division (+ warning when assigned to Decimal)" |
| R5 | `Decimal -> Number` is an error unless analysis proves it safe; when it rounds, both hosts round `MidpointRounding.AwayFromZero`. | **ruling clauses** — Q6: "Decimal -> Number rounding only when analysis proves it safe, else an error"; OQ-3: "`MidpointRounding.AwayFromZero`, emitted explicitly in BOTH the simulator and the generated C#". **Open: "proves it safe" — decided in T-12 via V-05** |
| R6 | bounds are never `double`: a `Number` bound is `long`, a `Decimal` bound `decimal`; `ExactNumber` (planned, T-02) is decimal-backed and saturates to unbounded on overflow. | **#679 plan direction** (T-02 row: "Number bounds are long, Decimal bounds decimal, never double") + **ruling clause** — OQ-6: "decimal-backed and saturates to unbounded on overflow (never throws, never wraps)" |

`DMNUM001` and `DMNUM002` are identifiers owned by T-08, not ruling text. Every planned symbol is tagged
`planned (T-NN)`; nothing planned is described as current.

## Interim state, by slice

Today's column is as of `d16a33bf` (the plan's evidence was `32d6ecb8`; the rows below were re-read at
`d16a33bf`). The "Landed by" column mirrors the T-02…T-13 rows in #679.

| Rule | Today | Landed by |
|---|---|---|
| R0 | nothing asserts parity. | T-05 `GateParity`; closed by T-13 |
| R1 | `Unknown` on either side is compatible; the assign check returns silently, and an assigned arithmetic expression is `Unknown`. | T-04 keeps a provisional lenient flag; T-05 flips it |
| R2 | one `TypeCategory.Number` for both; messages print "Number" for Decimal. | T-03 model, T-04 messages |
| R3 | `null` accepted for Text/Guid regardless of optionality; `IsRequired` is the only optionality definition. | T-06 |
| R4 | no rule; every numeric result is a bare `Number` (call args, function bodies, Sum/Count; an assigned arithmetic expression stays `Unknown`, see R5). | T-03b/T-04 result typing; T-08 warning |
| R5 | an assigned arithmetic expression is `Unknown` and unchecked (this is #491's path); Decimal -> Number on a *typed* source errors with a "Number to Number" message (#581); rounding modes diverge per OQ-3's recorded correction. | T-08 fail-closed baseline (error), T-12 rounding |
| R6 | `double` at bound parse, at mixed compare, and in `ValueRange`. | T-02 (`ExactNumber`), then V-05 |

## P-05: which bound type goes with which column type

Derived from R2 + R6 (#679 plan direction); not a ruling. R2 and R6 together answer P-05 without a new
mapping: a `Number` property is `Int64` → `long` → `bigint`, a `Decimal` property is `decimal` →
`decimal`. The existing mapping already says so — `DomainTypeMapping.ToClrTypeName`
(`Poly/DomainModeling/Meaning/DomainTypeMapping.cs:12-22`) and `ToSqlColumnType`
(`Poly/DomainModeling/Meaning/DomainTypeMapping.cs:29-39`) — so P-05 derives the column type from the
property's type.

## Rules this file follows

These govern the file itself; they are the shape T-01 was accepted on. Every row in *Rules* names a
provenance or an `Open: decided in T-NN`. No ruling is paraphrased inside a "ruling clause" cell — the quote
is copied. No `checked`-region rule is restated (its ADR owns it). No planned symbol appears as current.
Citations are repo-relative with the commit they were read at.
