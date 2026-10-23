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
