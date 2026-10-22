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
