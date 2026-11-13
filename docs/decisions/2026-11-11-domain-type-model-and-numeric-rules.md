# Domain type model and numeric rules (Q5, Q6, OQ-3, OQ-6, OQ-11)

Status: **Transcription of owner rulings Q5, Q6, OQ-3, OQ-6, OQ-11, plus the two contract clauses T-02 decided and the one T-03 decided (see *Open or newly decided*, below). `ExprType` and `DomainTypeModel` have landed (T-03, internal; the analyzer is its production caller since T-04). T-03b landed the Introspection contract for conversions and operators (internal; the analyzer calls `GetOperator` since T-04 to state a `BinaryTyping`, as a stamp and never a gate).**

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
`Number`, which wraps outside `checked` per Q6.** The checked-region ADR's bullet "`Decimal` does not wrap at
all (OQ-6 …)" (`docs/decisions/2026-11-11-checked-arithmetic-region.md:78-80`) therefore does not rest on
OQ-6; that bullet is F-07's to correct.

### OQ-11 — operator lookup (T)

> `OperatorTable.cs` maps syntax -> operator kind ONLY. Operand typing and result resolution go through a new
> Introspection `GetOperator` lookup owned by T. Built-in numeric/temporal operators are defined on the
> built-in types; value-type overloads are value-type members.

— OQ-11 row, [#659 comment 6805267269](https://github.com/scoizzle/Poly/issues/659#issuecomment-6805267269).

Landed (T-03): `Meaning/OperatorTable.cs` maps syntax → `OperatorKind` only (the enum lives in
`Poly/Introspection/OperatorKind.cs`). Landed (T-03b): an Introspection `GetOperator` lookup owns operand
typing and result resolution, as members of `ITypeDefinition` with the .NET rules under
`Poly/Introspection/CommonLanguageRuntime/` (see *Owner rulings of 2026-11-12*, below). OQ-11's "built-in
numeric/temporal operators are defined on the built-in types" holds for the numeric, Boolean and String
primitives through `ClrBuiltInRules`; the temporal operators are the ones the CLR types declare, and the
offset forms no host member expresses (`Date ± Number`, `Date + duration`) stay with the temporal library
until T-10.

## Rules

Legend. **ruling clause** — copied from a quote above, never paraphrased. **#679 plan direction** — the
owner-approved plan, not a ruling. **Open** — not settled by either; the named slice decides it.

| # | Rule | Provenance |
|---|---|---|
| R0 | define/check acceptance is the one gate: what it accepts, simulate, printed C# and polyc accept. | **ruling clause (Q5: gate) + #679 plan direction (host parity)** — Q5: "domain analysis is the one gate"; #679 goal: "anything define/check accepts compiles on the VM, in generated C# and in polyc". The harness is T-05 `GateParity`, closed by T-13 |
| R1 | `Unknown` is never compatible; the model's `ExprType.Unresolved` (landed, T-03; in use by the analyzer since T-04) is never assignable — not a wildcard. | **#679 plan direction** (goal: "`Unknown` never compatible"; contract: `ExprType.Unresolved` is never assignable/comparable). Q5 says replace the model; it does not say this |
| R2 | `Decimal` ≠ `Number`; `Number` ≡ `Int64`. | **#679 plan direction** — "Decimal distinct from Number" in the goal, and "Number ≡ Int64" in T-03's red test; the existing mapping (below) already spells `Number` as `long`/`bigint` |
| R3 | optionality is a flag beside the definition, not a wrapped type; `null` only into an optional target. | **#679 plan direction** — goal: "first-class optionality"; the shape is the `ExprType(Definition, Optional, DomainName)` contract (T-03) and T-06's `OptionalTyping` row |
| R4 | `Number / Number` is integer division; assigning it to `Decimal` warns. | **ruling clause** — Q6: "Number / Number is integer division (+ warning when assigned to Decimal)" |
| R5 | `Decimal -> Number` is an error unless analysis proves it safe; when it rounds, both hosts round `MidpointRounding.AwayFromZero`. | **ruling clauses** — Q6: "Decimal -> Number rounding only when analysis proves it safe, else an error"; OQ-3: "`MidpointRounding.AwayFromZero`, emitted explicitly in BOTH the simulator and the generated C#". **Open: "proves it safe" — decided in T-12 via V-05** |
| R6 | bounds are never `double`: a `Number` bound is `long`, a `Decimal` bound `decimal`; `ExactNumber` (T-02, `Poly/DomainModeling/Analysis/ExactNumber.cs`) is decimal-backed and returns `null` (unbounded) on overflow or on an inexact decimal result. | **#679 plan direction** (T-02 row: "Number bounds are long, Decimal bounds decimal, never double") + **ruling clause** — OQ-6: "decimal-backed and saturates to unbounded on overflow (never throws, never wraps)". **Landed in T-02 at parse and in the assign guard; analysis-time bound arithmetic (`ValueRange`, mixed `ConstraintMerge` compares) still uses `double` — V-05**. **Landed in V-05b**: analysis-time arithmetic is exact (`ValueRange(decimal? Min, decimal? Max)`; no `double`). Binary break on six public types: `ValueRange`, `EffectPostcondition.ValueRange`, `ActionStageInvariant.NarrowedRanges`, `AbstractValue.NumericRange`, `EffectInvariantQueries.CombinedRanges`, `StorageModel.VerifiedRange` (`double?` to `decimal?`); the version bump is the owner's decision (Q3). |

`DMNUM001` and `DMNUM002` are identifiers owned by T-08, not ruling text. Every planned symbol is tagged
`planned (T-NN)`; nothing planned is described as current.

Open or newly decided, named by the contract (an open item names the slice that decides it; T-02 decided two below and T-03 one):

- **Mixed `Number op Decimal` result type** — decided by T-03b: `Decimal`. `Decimal + Int64` resolves to the
  declared `decimal.op_Addition(decimal, decimal)` because `Int64` converts implicitly to `Decimal`.
- **`Float64 op Decimal`** — decided by T-03b: none. Neither type converts implicitly to the other, which is C#.
  Nothing else agrees today (the ledger): the lowered-tree analysis types it `Decimal` (T-17, T-18) and the ABI
  executes it as `Decimal` (T-21); the domain DSL cannot state a Float operand, so its analyzer column is n/a.
- **The built-in set answers what C# defines** (T-24). The contract answers every conversion and operator C#
  defines for built-in types: the §10.2.3 implicit numeric conversions and §10.3.2's explicit ones, conversion into
  `object` from any other CLR type (§10.2.8, §10.2.9), unary and binary numeric promotion (§12.4.7) through one
  `Promote` function, arithmetic, modulo, comparison, bitwise, shift (the count converts implicitly to `int`,
  §12.11), unary minus and plus, the Boolean operators, and `string + object` (§12.10.5). Nine `OperatorKind`s
  were added for modulo, unary minus and plus, bitwise and shift. The C# runtime binder is the test oracle: 51
  implicit and 81 explicit of the 132 ordered numeric pairs, and 3,584 operator cells. Decimal's conversions and
  operators stay the CLR's declared members. Deliberately none: enum, lifted, reference-equality, delegate and
  object operators.
- **`GetOperators(kind, other)` is pair-aware** — decided by T-24, not a tie-break. `UInt32 + Int32` is `Int64`,
  which neither operand owns, and a "most specific" tie-break over per-operand sets cannot rank `Byte + Byte`
  between `int` and `uint` without §12.6.4.7's C#-only rule in neutral code. `other` is neutral (`ITypeDefinition?`;
  null is the same type).
- **`Text + X` is language policy** — decided by T-24 and the orchestrator's ruling: the platform defines
  `string + object` for every CLR operand, and Poly allows only `Text + Text`. The provider states the platform
  fact; the domain analyzer's rejection of the 16 `Text + X` cells is a permanent, named ledger rule (T-07).
- **`Text` operators** — decided by T-03b: `Text + Text` is `Text`, equality is defined, and ordering and
  subtraction are none (#631 is T-07's).
- **Implicit `Number -> Decimal` widening** — decided by T-03: `Assign(Number -> Decimal)` is `Widening`,
  found through `decimal.op_Implicit(long)` via `ResolveConversionFrom`; `Decimal -> Number` is
  `RoundingNarrowing` through the explicit operator. Implied by Q6's "+ warning when assigned to Decimal".
- **What "assigned" covers for R4's Decimal warning** — `Open: decided in T-08`; #679's red test covers
  `assign` only, not call arguments, function returns or defaults.
- **A declared `Number` bound outside `long` range** is an **error**, decided by T-02: #515's silent
  acceptance is fixed at parse time with a positioned "out of range" message, and OQ-6 saturation is
  analysis-time arithmetic (`ExactNumber`), not literal parsing.
- **`Int32` / `Float` / `Double` bound types** take `decimal`, never `double`, decided by T-02 (A1): a
  non-Number-family declared type parses both bounds as `decimal` so one constraint never holds mixed
  bound types; column types remain P-05's.

## Interim state, by slice

Today's column is as of `12aa6c9d` (`origin/master` at the fix round; the plan's evidence was `32d6ecb8`, the
transcription `d16a33bf`). Every Today cell carries its file anchors at that commit. The "Landed by" column
mirrors the T-02…T-13 rows in #679.

| Rule | Today | Landed by |
|---|---|---|
| R0 | no general "analysis accepts ⇒ every host accepts" gate exists; specific parity tests do — `Poly.Tests/DomainModeling/Compile/SampleRoslynGateTests.cs:14`, `Poly.Tests/DomainModeling/Compile/UniqueSimulatePrintAgreeTests.cs:20`, `Poly.Tests/DomainModeling/Lowering/ActionParameterEnvelopeParityTests.cs:13`, `Poly.Tests/DomainModeling/Lowering/DurationDefaultParityTests.cs:21`, `Poly.Tests/Interpretation/AstParityTests.cs:21`. | T-05 `GateParity`; closed by T-13 |
| R1 | `Unknown` on either side is compatible — `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs:785-786`; the assign check returns silently, `:585-586`; and an assigned numeric arithmetic expression is `Unknown`, `:696`. | T-04 keeps a provisional lenient flag; T-05 flips it |
| R2 | one `TypeCategory.Number` for both — `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs:750`; messages print "Number" for Decimal, `:768`. | T-03 model, T-04 messages. **Resolved by T-04:** `TypeCategory` is gone; a Decimal is named "Decimal" in messages (#581). |
| R3 | `null` accepted for Text/Guid regardless of optionality, and rejected into a Number/Boolean/Date target even when optional (`CheckCompatible` has no optionality input) — `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs:787-790`, `:541-600`; `IsRequired` is the only optionality definition — `Poly/DomainModeling/Analysis/EntityStructureAnalyzer.cs:135-136`. | T-06 |
| R4 | no rule; every numeric result is a bare `Number` (call args, function bodies, Sum/Count — `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs:133`, `:357`, `:716`; an assigned numeric arithmetic expression stays `Unknown`, see R5). | T-03b result typing (`GetOperator(...).Result`; `Int64 / Int64` is `Int64`); T-04 adopts it; T-08 warning |
| R5 | an assigned numeric arithmetic expression is `Unknown` and unchecked (this is #491's path) — `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs:585-586`, `:696`; Decimal -> Number on a *typed* source errors with a "Number to Number" message (#581) — `:768`; rounding modes diverge per OQ-3's recorded correction. | T-08 fail-closed baseline (error), T-12 rounding |
| R6 | `double` at `Number` bound parse — `Poly/DomainModeling/Language/DomainResolver.Constraints.cs:100-113`; at mixed-type compare only, because same-type compare uses the type's own ordering — `Poly/DomainModeling/Ontology/Constraints/ConstraintMerge.cs:241-245`; and in `ValueRange(double?, double?)` — `Poly/DomainModeling/Analysis/EffectInvariantAnalyzer.cs:12`. Decimal bounds already parse as `decimal` — `Poly/DomainModeling/Language/DomainResolver.Constraints.cs:108-111`. | **Landed in T-02**: parse (`long`/`decimal`, `ExactNumber.TryParseDecimalText`) and the assign guard (`ExactNumber.Compare`); V-05 then removes the remaining `double` in analysis-time arithmetic. **Landed in V-05b**: the `ValueRange(double?, double?)` and `NumericBound` double paths are removed (same six-type binary break; version bump is the owner's Q3); the mixed-compare `double` survives only as `ConstraintMerge.TryCompare`'s fallback for a public API-built `RangeConstraint(double, double)`. |

## Owner rulings of 2026-11-12: the type provider supplies the platform's conversion and operator rules

Recorded by T-03b from [#679 comment 6837489066](https://github.com/scoizzle/Poly/issues/679#issuecomment-6837489066),
[6837522801](https://github.com/scoizzle/Poly/issues/679#issuecomment-6837522801) and
[6837577384](https://github.com/scoizzle/Poly/issues/679#issuecomment-6837577384). These are the owner's, not this file's.

- **Diagnosis.** The type system did not compute the resultant type of an expression in one place, so promotion
  and conversion were handled separately wherever they were needed; nine copies disagreed (`Float64 × Decimal`
  was accepted by the domain analyzer, typed `Decimal` by the lowered-tree pass, resolved in decimal's favour by
  the lambda bridge, and rejected by the VM).
  *Corrected by T-03b's ledger (2026-11-12):* the domain DSL cannot state a Float operand, so the domain analyzer's
  column is n/a; the lowered-tree pass types it `Decimal`; the ABI runs it as `Decimal` (it does not reject it).
- **Ruling 1.** The rules live with the type provider's definitions: the .NET definitions answer what converts
  to what and what an operator produces from reflection plus the built-in rules .NET leaves to the compiler;
  modeled types answer from declared members. Language policy stays in `DomainTypeModel`; the common result
  type of several operands is derived from those answers and the policy (T-03b ships it as `GetCommonType`);
  storage representation stays a VM fact.
- **Ruling 2.** The interface is in `Poly/Introspection/` (members of `ITypeDefinition`, internal today) and the
  .NET implementation is in `Poly/Introspection/CommonLanguageRuntime/`. Everything else consumes the interface
  only.
- **The long-term goal.** The VM and Introspection should be general enough to simulate almost any platform,
  provided the right foundational layers are supplied through the common interfaces. .NET is the first platform,
  not the only one the design may assume. The members are `internal` until the first non-CLR platform lands;
  going public is non-breaking.
- **Tree states vs ABI executes (R6).** What the tree states (operand types, the operator chosen, every
  conversion, as explicit nodes whose meaning comes from the interface) stays distinguishable from what the
  VM's ABI executes. The end state: the ABI executes exactly what the tree states, or rejects it; it never
  decides meaning. T-03b's cross-check ledger reports both, and a cell where the ABI did something the tree did
  not state is its own ledger kind. Row T-21 removes emitter-side promotion that decides meaning; L-03v's
  emit-time rejection is its first instance.
- **Convergence.** One row per remaining copy after T-04: T-16 (done: overload scoring reads the provider's conversions, and
  `IsNumericWideningFrom`, `NumericWidenRank` and the public `GetConversionFrom` door are gone; what remains outside the contract
  is listed in `Poly/Introspection/README.md`: the tier-sum scoring, the boxing rule stated twice and the analyzer's
  `IsImplicitlyConvertible(Type, Type)` residuals, none with a removing row yet; `IsCollectionShape` and
  `GetElementType` also name CLR types but do not answer a conversion), T-17 (`PromoteNumeric`/`GetNumericRank`/`ResolveAddType`),
  T-18 (`SyntaxTypeCompatibilityAnalyzer`), T-19 (`LambdaBridge.PromoteNumeric`), T-20
  (`ConstantFoldingPass.Promote`), T-21 (the emitter's promotion).

Landed shape (T-03b): `TypeConversion(Kind, Method?)` (null `Method` means the platform applies it),
`DeclaredOperator(Signature, PlatformApplied)`, `OperatorResolution(Signature, PlatformApplied)` with `Result`,
`GetOperator(kind, right)` with the identity-then-unique tie rule, and `GetCommonType(types)`. `And` and `Or` have
no declared form; the `op_*` names are Poly's modeled convention, not a platform fact. The closed numeric set
{Int32, Int64, Float64} moved verbatim from the interim `DomainTypeModel.BuiltInNumeric`, which is deleted; T-24
replaced it with the full C# table and made `GetOperators(kind, other)` pair-aware. `Int64 → Float64` stays
implicit and lossy above 2^53.

## P-05: which bound type goes with which column type

Derived from R2 + R6 (#679 plan direction); not a ruling. R2 and R6 together answer P-05 without a new
mapping: a `Number` property is `Int64` → `long` → `bigint`, a `Decimal` property is `decimal` →
`decimal`. The existing mapping already says so — `DomainTypeMapping.ToClrTypeName`
(`Poly/DomainModeling/Meaning/DomainTypeMapping.cs:12-22`) and `ToSqlColumnType`
(`Poly/DomainModeling/Meaning/DomainTypeMapping.cs:29-39`) — so P-05 derives the column type from the
property's type. `ToSqlColumnType` is the generic default, and its own contract says vendor packs override
it through `TypeMappingRegistry` (`Poly/DomainModeling/Meaning/DomainTypeMapping.cs:24-39`), so this paragraph
settles the bound type, not the DDL; #687's open sub-decision 1 (SQLite `NUMERIC` vs `TEXT` for Decimal)
stays open for P-05.

## Rules this file follows

These govern the file itself; they are the shape T-01 was accepted on. Every row in *Rules* names a
provenance or an `Open: decided in T-NN`. No ruling is paraphrased inside a "ruling clause" cell — the quote
is copied. No `checked`-region rule is restated (its ADR owns it). No planned symbol appears as current.
Citations are repo-relative with the commit they were read at.
