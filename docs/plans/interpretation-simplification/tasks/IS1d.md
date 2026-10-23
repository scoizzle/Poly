# TASK IS1d - a `for` increment prints as an expression, or the kind rejects

Status: planned. Branch `slice/IS1d`. Lane A (`csharp`). Depends on IS1. Found during IS1.
Plan: [IS-README](../IS-README.md).

## Why

IS1 could not claim `Writer` for `ForLoop`, because the writer prints invalid C# for it:

```csharp
// ForLoop with Increment = i = i + 1
for (var i = 0; i < 3; i = i + 1) { }          // intended
for (var i = 0; i < 3; var i = i + 1) { }      // printed
```

The increment goes through statement text, so the writer's "fuse an undeclared name" path emits a `var` declaration inside a `for` header. `CS0815`/`CS1026` territory: the emitted file does not compile.

This is the same class of defect IS0a exists to remove — the writer producing a wrong shape instead of failing closed. It survived because **no golden contains a `for` loop** (`grep -rl "for (" Poly.Tests/DomainModeling/Compile/EmitGolden/` is empty), so neither the golden corpus nor the Roslyn comparison ever printed one.

## Scope

- Print the increment (and the initializer, if it is an expression) as an **expression**, not a statement. Whatever suppresses the `var` fusion inside a `for` header must be scoped to the header only — the body keeps its normal rules.
- Re-run IS1's totality test with `Producers.Writer` added to `ForLoop`'s row. It must fail before the fix and pass after.
- Add a golden file containing a `for` loop, so the corpus can never go blind on this kind again. Check first whether an existing golden directory is asserted 1:1 against a domain's emit (IS0b hit this and created a new directory).
- If the increment cannot be printed faithfully for some shape, that shape rejects — do not approximate it.

## Files

- `Poly/Interpretation/CSharp/CSharpWriter.cs` — `ForText` and the fusion scope.
- `Poly.Tests/Interpretation/CSharpLanguageTests.cs` (or the class that owns `for` printing) — the tests.
- `Poly/Interpretation/NodeFacts.cs` — `ForLoop` gains `Producers.Writer`.
- `Poly.Tests/DomainModeling/Compile/EmitGolden/**` — one golden with a `for`.

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| `for (var i = 0; i < 3; i = i + 1) body` | `i = i + 1` prints as `var i = i + 1` | prints as the expression | `ForIncrement_PrintsAsAnExpression` |
| that text through Roslyn | syntax errors | zero syntax diagnostics | `ForHeader_ParsesWithRoslyn` |
| `Write(Read(x))` for a for loop | header mismatch | idempotent | `ForLoop_WriteRead_RoundTrips` |
| the loop body's first assignment | fused `var` (correct) | unchanged | `ForBody_StillFusesTheDeclaration` |

## Done when

- `ForLoop` is `Producers.Writer` in `NodeFacts` and the totality test passes.
- A golden contains a `for` loop and is green.
- The body's fusion behaviour is unchanged (the last row above).
- Full suite green; build 0 warnings.

## SHIP if / NOT SHIP if

SHIP: printed `for` headers compile, the golden exists, and the fusion change is scoped to the header.
NOT SHIP: the fix suppresses `var` globally; a body assignment lost its declaration; the golden was added without a `for` in it.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/NodeFactsTotalityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpLanguageTests/*'
```
Then the full suite.

## Hand-edit

The header prints two expressions and the body prints statements; the difference is visible in one function.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-22 | agent | Copilot/VS Code | `d42fdf25` | SHIP — the `for` condition and increment print as expressions; `ForLoop` is `Producers.Writer`; one golden carries a `for` | RED: `CSharpLanguageTests` 5 failed of 201 (`ForIncrement_PrintsAsAnExpression` received `for (var i = 0; i < 3; var i = i + 1)`), `NodeFactsTotalityTests` 1 failed of 8 (`CorpusKind_ClaimedByWriter_RoundTrips`). GREEN: `CSharp*` 473/473, totality 8/8, full suite 4331 passed 0 failed, build 0 warnings. |

### Evidence (2026-10-22)

RED — `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpLanguageTests/*'` (five new tests and the new golden in place; `ForBody_StillFusesTheDeclaration` already passed, as row 4 of the shape matrix requires):

```
failed Read_GoldenFile_RoundTripIsIdempotent(.../EmitGolden/handwritten/For.cs.golden) (16ms)
failed ForIncrement_PrintsAsAnExpression (1ms)
  AssertionException: Expected to contain "for (var i = 0; i < 3; i = i + 1)"
  but received "#nullable enable
  
  
  for (var i = 0; i < 3; var i = i + 1)
  { }"
failed ForCondition_Assignment_PrintsAsAnExpression (3ms)
  AssertionException: Expected to not contain "var ready"
  but received "#nullable enable
  
  
  for (var i = 0; var ready = i < 3; var i = i + 1)
  { }" which contains "var ready"
failed ForHeader_ParsesWithRoslyn (3ms)
  AssertionException: Expected to be empty
  but collection contains items: [CS1003 Syntax error, ',' expected]
failed ForLoop_WriteRead_RoundTrips (0ms)
  FormatException: Expected statement, got 'for' (line 4, col 1)
     at Poly.Interpretation.CSharp.CSharpReader.Parser.ReadUnit()
Test run summary: Failed!  total: 201 / failed: 5 / succeeded: 196
```

RED — `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/NodeFactsTotalityTests/*'`, with `Producers.Writer` added to `ForLoop`'s row:

```
failed CorpusKind_ClaimedByWriter_RoundTrips (1s 852ms)
  AssertionException: Expected to be empty, because the writer's own output must read back:
  Poly.Tests/DomainModeling/Compile/EmitGolden/handwritten/For.cs.golden: FormatException: Expected statement, got 'for' (line 4, col 1)
Test run summary: Failed!  total: 8 / failed: 1 / succeeded: 7
```

GREEN — `--treenode-filter '/*/*/CSharp*/*'`: `total: 473 / failed: 0`; `--treenode-filter '/*/*/NodeFactsTotalityTests/*'`: `total: 8 / failed: 0`; full suite `dotnet run --project Poly.Tests/Poly.Tests.csproj`: `total: 4331, failed: 0, succeeded: 4331, skipped: 0`; `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`: `0 Warning(s), 0 Error(s)`.

Printed header, before → after:

```
for (var i = 0; i < 3; var i = i + 1) { }      // before: CS1003, unreadable
for (var i = 0; i < 3; i = i + 1) { }          // after
```

### Findings and deviations

1. **The suppression is `ForHeaderExpression`, applied to the condition and the increment.** It clears `_fuseUndeclaredName` for the duration of one `Expr` call and restores it in `finally`, so `BodyText` — which runs after the header — prints under the enclosing rule and `ForBody_StillFusesTheDeclaration` stays green. The card names the increment; the condition is the same defect one slot over (`for (var i = 0; var ready = i < 3; …)`), and `ForCondition_Assignment_PrintsAsAnExpression` pins it (a fifth test beyond the four).
2. **The init keeps its existing treatment, deliberately.** Its declared branch renders the `for-init var-decl` pattern, and a plain-assignment initializer still fuses at top level: a declaration is legal in the init slot, and `for (var i = 0; …)` is the only spelling that compiles when `i` is declared nowhere else. Suppressing fusion there would trade a wrong-shape defect for a CS0103.
3. **A statement or block in a header slot rejects.** `Expr` prints a `Block` as a block, which would put a statement where the grammar allows one expression; `ForHeaderExpression` rejects it through `Unprintable`, naming the kind and the node id, and `ForIncrement_StatementInsteadOfExpression_Rejects` holds that. Nothing the reader can produce is affected — `for-incr` and `for-cond` fold expressions only.
4. **The golden lives in `EmitGolden/handwritten/For.cs.golden`, next to IS0b's `Switch.cs.golden`.** Every other directory there is asserted 1:1 against a sample domain's emit (`Emit_MatchesGolden`), and no domain can host this file: the reader is the only producer of `ForLoop` (no lowering emits one). Its text is a top-level program, so the golden exercises the fusion path that caused the defect; `Read_GoldenFile_RoundTripIsIdempotent` asserts the printed form parses.
5. **No `NodeFactCorpus.AddedTexts` entry was needed.** The golden ends `.cs.golden`, so it is already a writer subject, and it is the corpus case that evidences the `Writer` claim.

