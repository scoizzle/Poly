# TASK IS7g - a constant with no literal prints its qualified form

Status: planned. Branch `slice/IS7g`. Lane A (`csharp`). Depends on IS2d (merged).
Plan: [IS-README](../IS-README.md). Precedent in the writer: `DoubleLiteral`.

## Why

`LiteralText` prints an integral constant as `<digits>L`, which for `long.MinValue` is **not valid C#**:

```csharp
_ = -9223372036854775808L;   // CS0266: 9223372036854775808 is a negated POSITIVE literal
```

IS2f recorded this as a known limit rather than fixing it. It is fixable, and the writer already solves the same problem one arm away: `DoubleLiteral` (`CSharpWriter.cs:1413-1422`) emits `double.NaN`, `double.PositiveInfinity` and `double.NegativeInfinity` rather than a literal, because those have no literal either. `long.MinValue` is the same case, so printing `long.MinValue` closes an inconsistency rather than inventing a form.

`System.Int64.MinValue` is the fully-qualified alternative. **Prefer the keyword form** — the writer already prints primitive keywords elsewhere (`PrimitiveTypeReference` → `GetCSharpKeyword()`), and `long.MinValue` is what a person writes.

## Scope

1. **Writer.** `LiteralText`'s `long` arm prints `long.MinValue` for `long.MinValue`. Verify by **compiling**, not by reasoning, which other constant values fail: `int.MinValue` is special-cased by the C# spec at the negated-literal position (`-2147483648` is a legal `int` constant), so it probably needs nothing — but confirm, and confirm `ulong`/`uint`/`short`/`sbyte`/`byte` too. Only add a qualified form where a literal genuinely fails to compile.
2. **Reader.** Teach the fold so the qualified form comes back as the same `Constant`. `NodeFacts` claims `Writer` for `Constant` as "prints it and the printed text re-reads to the same kind", so printing a form the reader cannot fold would break that claim for this value. Check whether `double.NaN` already has this gap — if it prints but does not fold to `Constant(double.NaN)`, fix that here too, with the same mechanism.
3. **Rewrite the pinned limit.** `RangeAttributeArgumentsTests.LongMinValue_HasNoPrintableCSharpLiteral_KnownLimit` asserts the broken text on purpose. It becomes an assertion that the qualified form is emitted, compiles, and round-trips — with a comment saying the limit was real and is now closed.

## Files

- `Poly/Interpretation/CSharp/CSharpWriter.cs` - `LiteralText`.
- `Poly/Interpretation/CSharp/CSharpGrammar.cs` / `CSharpReader.cs` - the fold, if the reader does not already handle `double.NaN`-style qualified constants.
- `Poly.Tests/Interpretation/CSharpLanguageTests.cs` or a new `QualifiedConstantTests.cs`.
- `Poly.Tests/DomainModeling/Lowering/RangeAttributeArgumentsTests.cs` - the rewritten limit.
- `Poly/Interpretation/NodeFacts.cs` - only if a claim changes.

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| `Constant(long.MinValue)` written | `_ = -9223372036854775808L;` (CS0266) | `_ = long.MinValue;` | `LongMinValue_PrintsTheQualifiedForm` |
| that text compiled | fails | zero diagnostics | `LongMinValue_PrintedTextCompiles` (**compile**, not parse) |
| that text read back | n/a | `Constant(long.MinValue)` | `LongMinValue_RoundTripsToTheSameConstant` |
| its value | n/a | `long.MinValue` | `LongMinValue_YieldsTheSameValue` |
| `Constant(int.MinValue)` | `-2147483648` | unchanged, unless it fails to compile | `IntMinValue_UnchangedOrQualified` |
| `Constant(double.NaN)` | `double.NaN` | unchanged; now folds if it did not | `NaN_PrintsAndFolds` |
| a plain `Constant(7L)` | `7L` | unchanged | existing corpus |

## Done when

- Every row passes; the compile row uses a real Roslyn compile — `ParseText` does not report CS0266 (the IS0b lesson), so a parse-only assertion would pass on the broken text.
- No constant value the writer accepts produces text that fails to compile.
- `AstParityTests` 145/145; full suite green; `dotnet build Poly.Benchmarks` 0 warnings.
- `NodeFacts`' `Writer` claim for `Constant` still holds — verified by the corpus round-trip test, not by argument.

## SHIP if / NOT SHIP if

SHIP: the qualified form compiles, folds to the same `Constant`, and no other constant regressed to an uncompilable literal.
NOT SHIP: a parse-only test is used as the compile evidence; `int.MinValue` is changed without proof it needed it; the pinned limit test is deleted rather than rewritten.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*Qualified*/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpLanguageTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/RangeAttributeArgumentsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/NodeFactsTotalityTests/*'
```
Then the full suite.

## Hand-edit

One arm prints the qualified form; one fold reads it back; the compile test is the proof.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-23 | agent | Copilot/VS Code | `64de3576` | **RED.** `--treenode-filter '/*/*/*Qualified*/*'`: 14 tests, 3 failed, 11 passed. | The compile row was **not** red, and the card's premise is wrong: `-9223372036854775808L` compiles and is typed `long`. The real defect is the reader overflow. Details below. |
| 2026-10-23 | agent | Copilot/VS Code | `64de3576` | **SHIP.** Qualified form printed and folded; full suite **4,541 passed / 0 failed / 0 skipped**; `Poly.Benchmarks` build 0 warnings. | `NodeFacts` untouched — `Constant` already claimed `Writer | Reader`, and the change makes that claim true for these values instead of weakening it. |

### Red first (2026-10-23)

Three failures, all real:

1. `LongMinValue_PrintsTheQualifiedForm` — expected `_ = long.MinValue;`, received
   `_ = -9223372036854775808L;`.
2. `LongMinValue_RoundTripsToTheSameConstant` —
   `OverflowException: Value was either too large or too small for an Int64.` at
   `CSharpReader.FoldNumber` (`CSharpReader.cs:1141`, the `long.Parse` on `9223372036854775808L`),
   reached through `FoldPrimary` ← `FoldPostfix` ← `FoldUnary`. **This is the real defect:** the
   reader overflowed on the text the writer had just produced, so `Read(Write(x))` threw.
3. `NaN_PrintsAndFolds` — expected 1 `Constant`, found 0. `double.NaN` printed but read back as
   `Member`, so the same `Writer` claim was false for it too. Same bug, fixed by the same mechanism.

Four tests were green before the change and stay green: `LongMinValue_PrintedTextCompiles`,
`LongMinValue_YieldsTheSameValue`, `IntMinValue_UnchangedOrQualified`,
`EveryConstantKind_PrintedTextCompiles`. They are guards, not discriminators — see the premise
correction.

### Premise correction: CS0266 did not reproduce (2026-10-23)

The card says `-9223372036854775808L` is CS0266 and "the printed program does not compile". Compiling
says otherwise, so the fix is justified by the round-trip, not by a compile failure. Roslyn compiles
the emitted text with TPA references:

| Probe | Diagnostics | Semantic type |
|---|---|---|
| `_ = -9223372036854775808L;` | none | `long` |
| `long v = -9223372036854775808L;` | none | `long` |
| `long v = long.MinValue;` | none | `long` |
| `long v = 9223372036854775808L;` (control) | **CS0266** | — |
| `var v = 9223372036854775808L;` | none (it is `ulong`) | `ulong` |
| `double v = double.NaN;` | none | `double` |
| `long[] v = new long[] { -9223372036854775808L };` | none | `long[]` |

The control proves the probe detects CS0266; the negated form is accepted because the positive
literal is only `ulong` **on its own**, and Roslyn's negated-literal rule types
`-9223372036854775808L` as `long`. So the digits spelling is legal C# that the writer's own reader
could not read — the `Writer` claim was broken by the *read* direction, which is the half the card
required anyway.

### Compile results per constant type (2026-10-23)

Thirty boundary values, each compiled in a method body (`CSharpCompilation.GetDiagnostics()`, errors
only): `long.MinValue/MaxValue/0`, `int.MinValue/MaxValue`, `short.MinValue/MaxValue`,
`sbyte.MinValue/MaxValue`, `byte.MaxValue`, `ushort.MaxValue`, `uint.MaxValue`, `ulong.MaxValue`,
`decimal.MinValue/MaxValue`, `double` NaN/±∞/MinValue/MaxValue, `float` NaN/±∞/MinValue/MaxValue,
`bool`, `string`, `char`, and `long.MinValue` inside a 1-D array and inside a `List<long>`.
All zero diagnostics, before and after — **only `long.MinValue` changed.** The array and `List`
rows embed the element text through `LiteralText`, so they gained the qualified form for free.

Only `long.MinValue` needed a qualified form. `int.MinValue` keeps `-2147483648` (legal at the
negated-literal position); `ulong`, `uint`, `short`, `sbyte`, `byte`, `decimal` and the float specials
all compile as they are, so none was given one. Two harness-only findings: `_ = null;` is CS8183
(a discard cannot infer a type, which says nothing about the literal) and a `List` literal needs its
`using`, so the sweep supplies one.

### Mechanism (2026-10-23)

Writer: `LiteralText`'s `long` arm calls `LongLiteral`, which prints `long.MinValue` for that one
value and `<digits>L` otherwise — the precedent `DoubleLiteral` set for values with no literal.
Reader: `FoldPostfixTails`' `member` case routes through a new `QualifiedConstant(receiver, member)`
that folds `(PrimitiveId, member)` for exactly `(Int64, MinValue)` and `Float64`/`Float32` ×
{`NaN`, `PositiveInfinity`, `NegativeInfinity`}. No grammar change, so ordinary `a.b` parses exactly
as before; `int.MaxValue` and `long.MaxValue` deliberately stay `Member` because the writer never
emits them and folding them would change a meaning the writer does not rely on.

### Green (2026-10-23)

`/*/*/*Qualified*/*` 14/14 · `CSharpLanguageTests` 201/201 · `RangeAttributeArgumentsTests` 19/19 ·
`NodeFactsTotalityTests` 8/8 · `AstParityTests` **146/146** · full suite **4,541 passed / 0 failed /
0 skipped** (4,534 before this change, +7 new tests) · `dotnet build Poly.Benchmarks` 0 warnings,
0 errors.

`AstParityTests` holds **146**, not the card's 145; that count was already 146 before this change and
no parity test was added.
