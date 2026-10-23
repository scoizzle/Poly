# TASK IS2f - `[Range]` picks the constructor the constraint calls for

Status: **in flight** — implemented and green on `slice/IS2f`. Issue #281. Found by IS2d, which made the defect visible.
Plan: [IS-README](../IS-README.md). Lane: none of A/B/C — the change is in `src/Poly.DslCompiler/`, an artifact producer, not in CORE/Lower.

## Why

`MinimalApiGenerator.BuildConstraintAttributes` built every `[Range]` argument as a `double`:

```csharp
new Constant(r.Minimum is not null ? Convert.ToDouble(r.Minimum) : minBound),
new Constant(r.Maximum is not null ? Convert.ToDouble(r.Maximum) : maxBound)
```

so a `long` property constrained `range(0, 100000000)` emitted `[Range(0.0, 100000000.0)]` and bound
`RangeAttribute(double, double)`. A human writes `[Range(0, 100000000)]` and binds `RangeAttribute(int, int)`.

Two costs, and the second is the one that made it an issue:

1. **Semantics.** `RangeAttribute(double, double)` validates by converting the member to `double` — lossy
   past ±2^53. `(int, int)` is exact over its domain, and is what an author writing `range(0, 100)` on a
   `long` expects.
2. **The artifact.** Generated DTOs are read by humans. `[Range(0.0, 100000000.0)]` on an integral
   property reads as a mistake.

It was invisible until IS2d (#279): the writer used to print a whole-valued `double` with invariant `G`,
so `Constant(0.0d)` printed as `0` and bound the `int` constructor by accident. IS2d fixed the writer to
print the truth about the AST. The generator must now build the right AST.

## Scope

`RangeAttribute` offers only `(int, int)` and `(double, double)`, so the rule is one decision:

- **both bounds integral and within `int` range, on an integral target (`byte`/`short`/`int`/`long`)** →
  emit `int` constants, binding `(int, int)`;
- **otherwise** → emit `double` constants, binding `(double, double)` — deliberately, because a fractional
  bound or a bound beyond `int` has no integral constructor, and truncating to `int` would falsely reject
  valid values;
- **open bounds** fall back to the CLR type's own limits carried in that type, so `range(0, )` on `byte`
  is `[Range(0, 255)]` (integral) while `range(0, )` on `long` is `[Range(0.0, 9.223372036854776E+18)]`
  (the long's real max, not `double.MaxValue`, because `long.MaxValue` is beyond `int`).

`decimal`/`float`/`double` targets always take the double constructor: the target is not integral, so an
integral-looking bound would narrow validation the member does not have.

Out of scope, recorded only: `long.MinValue` has no printable C# literal.

## Files

- `src/Poly.DslCompiler/RangeAttributeArguments.cs` (new) — the rule and the CLR bound table, in one place.
- `src/Poly.DslCompiler/MinimalApiGenerator.cs` — `BuildConstraintAttributes` calls the rule; the old
  `ClrNumericBounds` double-tuple helper is gone.
- `src/Poly.DslCompiler/Poly.DslCompiler.csproj` — `InternalsVisibleTo("Poly.Tests")` so the rule can be
  proved directly for `short`/`byte` targets the DSL cannot name.
- `Poly.Tests/DomainModeling/Lowering/RangeAttributeArgumentsTests.cs` (new) — the shape matrix, rule-level
  and end to end, plus the `long.MinValue` known-limit pin.
- `Poly.Tests/DomainModeling/Lowering/DslCompilerSqliteOracleTests.cs` — four expectations.
- `Poly.Tests/DomainModeling/Compile/EmitGolden/crm-dslcompiler/Program.cs.golden` — one line.

## Shape matrix

| Case | Before | After | Test that proves it |
|---|---|---|---|
| `long` + `range(0, 100000000)` | `[Range(0.0, 100000000.0)]` | `[Range(0, 100000000)]` | `IntegralLongRange_EmitsIntegralConstructor`, `NumberRange_EmitsIntegralRangeAttribute` |
| `int` + `range(0, 100)` | `(double, double)` | `[Range(0, 100)]` | `IntegralIntRange_EmitsIntegralConstructor` |
| `short` + `range(-10, 20)` | `(double, double)` | `[Range(-10, 20)]` | `IntegralShortRange_EmitsIntegralConstructor` |
| `byte` + `range(0, 255)` | `(double, double)` | `[Range(0, 255)]` | `IntegralByteRange_EmitsIntegralConstructor` |
| `byte` + open `range(0, )` | `[Range(0.0, 255.0)]` | `[Range(0, 255)]` | `OpenMaximumOnByte_CapsAtByteMaxIntegrally` |
| `short` + open `range(0, )` | `[Range(0.0, 32767.0)]` | `[Range(0, 32767)]` | `OpenMaximumOnShort_CapsAtShortMaxIntegrally` |
| `int` + open `range(0, )` | `[Range(0.0, 2147483647.0)]` | `[Range(0, 2147483647)]` | `OpenMaximumOnInt_CapsAtIntMaxIntegrally` |
| `long` + open `range(0, )` | `[Range(0.0, 9.223372036854776E+18)]` | same text, now from the real `long` bounds; doubles deliberately | `OpenMaximumOnLong_UsesLongMaxNotDoubleMax`, `NumberOpenRange_CapsAtLongMaxAsDouble` |
| `long` + `range(0.5, 2.5)` | `[Range(0.5, 2.5)]` | unchanged, doubles deliberately | `FractionalBounds_OnIntegralTarget_EmitsDoubleConstructor`, `NumberFractionalRange_EmitsDoubleRangeAttribute` |
| `long` + `range(0, 5000000000)` | `[Range(0.0, 5000000000.0)]` | unchanged, doubles deliberately | `RangeBeyondInt_OnLong_EmitsDoubleConstructorDeliberately`, `NumberRangeBeyondInt_EmitsDoubleRangeAttributeDeliberately` |
| `double` target, integral bounds | `(double, double)` | unchanged, deliberately | `DoubleTarget_StaysDoubleConstructor` |
| `decimal` target | `(double, double)` | unchanged, deliberately | `DecimalTarget_StaysDoubleConstructor` |
| `float` target | `(double, double)` | unchanged, deliberately | `FloatTarget_StaysDoubleConstructor` |
| `decimal` + open `range(, )` | `[Range(-1.797…E+308, 1.797…E+308)]` | `[Range(-7.922816251426434E+28, 7.922816251426434E+28)]` | `OpenDecimalRange_CapsAtDecimalBoundsAsDouble` |
| `long.MinValue` as a `long` constant | prints `-9223372036854775808L` | unchanged — known limit, recorded not fixed | `LongMinValue_HasNoPrintableCSharpLiteral_KnownLimit` |

The `long`-open row is the one that cannot change: the rule's integral constructor is `(int, int)`, and
`long.MaxValue` does not fit an `int`. Capping the open max at `int.MaxValue` would falsely reject values
a `long` member accepts — a worse defect than the double's precision. What the fix does give that row is
the bound **source**: the long's real max rather than a double-range sentinel, which is what lets the
`byte`/`short`/`int` rows become integral.

## Done when

- The rule lives in one place; the call site holds no type decision.
- Every shape-matrix row has a test, and every row whose text changed was red first.
- Goldens and oracle expectations updated, one explanation per line.
- Full suite green; `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` reports 0 warnings.

## SHIP if / NOT SHIP if

SHIP: an integral constraint on an integral target emits the integral constructor; every deliberate double
has a test naming it; no emitted attribute narrows or widens a constraint it did not before.
NOT SHIP: a whole-valued `double` accepted as integral because the writer used to print it that way; a
beyond-`int` bound silently truncated; `long.MinValue` "fixed" by inventing a literal form.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/RangeAttributeArgumentsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DslCompilerSqliteOracleTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit

None. The one golden line and the four oracle expectations are hand-edited, explained in the Log.

## Needs Scot

Nothing blocking. Two records he may want to rule on:

1. **`long.MinValue`** still has no printable C# literal. Out of scope here; the range rule never reaches
   it, because a beyond-`int` bound takes the double constructor. If it ever matters, the writer needs a
   `-9223372036854775808` special case (or `long.MinValue.ToString()` plus a leading-`-` guard).
2. **`decimal` open ranges** now cap at `decimal.MinValue`/`MaxValue` rather than the double sentinels, as
   a consequence of the bounds table carrying real CLR bounds. Faithful to the type; no test asserted the
   old text.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-23 | Copilot | 2026-10-23 | — | RED | `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/RangeAttributeArgumentsTests/*'` → **9 failed, 10 succeeded** of 19. Failures and their reason: `OpenMaximumOnInt_CapsAtIntMaxIntegrally`, `IntegralLongRange_EmitsIntegralConstructor`, `OpenMaximumOnShort_CapsAtShortMaxIntegrally`, `IntegralByteRange_EmitsIntegralConstructor`, `NumberRange_EmitsIntegralRangeAttribute`, `OpenMaximumOnByte_CapsAtByteMaxIntegrally`, `IntegralIntRange_EmitsIntegralConstructor`, `IntegralShortRange_EmitsIntegralConstructor` — "Expected to be of type Int32 but Value is of type Double" / `InvalidCastException: Unable to cast object of type 'System.Double' to type 'System.Int32'`, and the end-to-end row's received text `[Range(0.0, 100000000.0)]` on `public long ListPrice`. Ninth: `LongMinValue_HasNoPrintableCSharpLiteral_KnownLimit` — received `_ = -9223372036854775808L;`, expectation corrected to the real print. To make the rule-level tests compile and fail for the *stated* reason rather than a compile error, the extraction landed first as a behaviour-preserving skeleton (always `double`). |
| 2026-10-23 | Copilot | 2026-10-23 | — | GREEN | Same filter → **19 passed, 0 failed**. Oracle slice `DslCompilerSqliteOracleTests` → 20 passed. Golden slice `EmitGoldenTests` → 21 passed. Full suite → **4,493 passed, 0 failed** (IS2d baseline 4,474 + 19 new). Build → 0 warnings. |
| 2026-10-23 | Copilot | 2026-10-23 | — | Golden + oracle diffs, per line | Golden `crm-dslcompiler/Program.cs.golden:1352`: `[Range(0.0, 100000000.0)]` → `[Range(0, 100000000)]` — `Product.ListPrice: Number range(0, 100000000)`; `0` and `100000000` are integral and fit `int`, so the exact constructor applies. Oracle `DslCompilerSqliteOracleTests`: `:285` `[Range(0.0, 1000.0)]` → `[Range(0, 1000)]` (action DTO inherits `Stock: Number range(0, 1000)`); `:448` `[Range(1.0, 50.0)]` → `[Range(1, 50)]` (`Qty: Number range(1, 50)`); `:426` the negative guard `DoesNotContain("[Range(0.0, 10.0)]")` → `DoesNotContain("[Range(0, 10)]")` — otherwise the guard would pass only because the shape changed, not because the defect is absent; `:396` `DoesNotContain("[Range(0.0, 0.0)]")` → `DoesNotContain("[Range(0, 0)]")`, same reason (the collapsed-open-max bug would now print the integral form). `:395` `Contains("[Range(0.0, 9.223372036854776E+18)]")` is unchanged and deliberately so — a `long`'s open max is beyond `int`. |
| 2026-10-23 | Copilot | 2026-10-23 | — | `long.MinValue` | Not handled: `CSharpWriter` prints `-9223372036854775808L`, which is a negated positive literal and does not compile. Recorded by `LongMinValue_HasNoPrintableCSharpLiteral_KnownLimit`; no production change. |
