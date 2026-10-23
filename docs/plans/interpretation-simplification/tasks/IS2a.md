# TASK IS2a - the writer, checked by Roslyn, over the AST corpus

Status: planned. Branch `slice/IS2a`. Lane A (`csharp`). Depends on IS1.
Plan: [IS-README](../IS-README.md). Audit: [interpretation-simplification.md](../../../analysis/interpretation-simplification.md) §2.

## Why

`LinqExpressionGenerator` is the only differential the VM has: the same tree is evaluated by a second in-house interpreter and the two answers are compared. It is an unsound oracle — fewer arms than the VM, accepts kinds the VM rejects, models `this` as `null`, and `VmCorrectnessTests.AssertVmMatchesLinq` returns early on "VM compile rejected" (`:345`), so "LINQ executes, VM rejects" is invisible by construction.

This card builds the replacement **before** anything is deleted: print the AST with `CSharpWriter`, compile the text with Roslyn (already referenced by `Poly.Tests`), execute it, and compare to the VM. One meaning-source, verified by a compiler that is not ours to argue with. [`ParityScenario`](../../../../Poly.Tests/TestHelpers/ParityScenario.cs) already proves the shape at the domain level; this is the same pattern one layer down.

The audit's correction is a requirement, not a caveat: **a writer→Roslyn route does not by itself verify the interpreter's semantics**, and it certifies garbage while the writer lies. IS0a/IS0b/IS1 are done before this card for that reason.

## Scope

- New harness `Poly.Tests/Interpretation/AstParity.cs`:
  - input: a `Node` program (expression or statement block) and its expected VM result;
  - print via `CSharpLanguage.Write(...)` — for a block, into a method body via the `Write(typeDefs, testStatements)` overload;
  - compile and load with [`ExportedCSharp.CompileAndLoad`](../../../../Poly.Tests/TestHelpers/ExportedCSharp.cs);
  - invoke, normalize the result the way the VM represents it, compare.
- **Never skip a VM rejection.** The old harness's early return is the bug. If the VM rejects, the harness asserts the writer or Roslyn rejects too, and records the pair as "rejected on both sides" — a difference in *which* side rejects is a failure, not a skip.
- **Assert the printed text parses with zero Roslyn syntax diagnostics** before executing it. A compile failure is a test failure with the emitted source in the message.
- Cover the corpus already in the tree: every program in `Poly.Tests/Integration/**` and `Poly.Tests/Interpretation/**` that currently goes through `NodeTestHelpers.BuildExpression`, plus the domain-level `ParityScenario` cases. Do not add new language features to make a case pass; an unsupported kind is a table rejection (IS1) and an explicit row here.
- Keep the LINQ oracle running in this PR. This card adds a second opinion; IS2c removes the first.

## Files

- New: `Poly.Tests/Interpretation/AstParity.cs`
- New: `Poly.Tests/Interpretation/AstParityTests.cs`
- `Poly.Tests/Poly.Tests.csproj` — only if a reference is genuinely missing (Roslyn is already there)

## Shape matrix

| Case | Before | After | Test that proves it |
|---|---|---|---|
| a tree the VM executes | compared to LINQ | compared to Roslyn-executed printed C# | one parity row per existing corpus program |
| printed text with a syntax error | not checked | test fails with the source | `PrintedText_HasZeroRoslynDiagnostics` |
| the VM rejects a kind the writer prints | invisible (early return) | failure | `VmRejects_AndPrintedExecutes_IsAFailure` |
| the VM executes a kind the writer cannot print | invisible | failure naming the kind | `VmExecutes_AndWriterRejects_IsAFailure` |
| a known unsupported kind | silently skipped | explicit rejection row | `UnsupportedKinds_AreListedNotSkipped` |

## Done when

- Every corpus program that the VM executes has a Roslyn parity row and agrees.
- The two asymmetry rows fail when the asymmetry is introduced (demonstrate one, record the red run in the Log).
- The harness never returns early on a rejection.
- Full suite green.

## SHIP if / NOT SHIP if

SHIP: the harness compares both sides for every corpus program and treats rejection asymmetry as failure.
NOT SHIP: a rejection is skipped; the corpus is a new hand-written list rather than the existing programs; `LinqExpressionGenerator` was deleted here (IS2c).

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmCorrectnessTests/*'
```
Then the full suite.

## Hand-edit

One harness class: print, parse, run, compare, and the failure message prints the emitted source.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-22 | agent | Copilot/VS Code | b3ede88c | **SHIP.** `AstParity` is the differential: VM execution vs `CSharpWriter`-printed text that Roslyn parses, compiles, and runs. 128 corpus programs (one row per existing `BuildExpression` program, 16 files) + 7 harness contracts, all green; full suite 4,473 passed / 0 failed / 0 skipped; `Poly.Benchmarks` build 0 warnings. 29 corpus rows do not agree yet — each pinned in `AstParity.KnownDivergences` with its defect (below). `LinqExpressionGenerator` untouched, still running alongside. | **11 writer defect classes, none previously known:** (1) `double` constants print with `G`, so `7.0`→`7` (integer division) and `NaN`→`NaN` (CS0103); (2) unary minus of a negative constant prints `--99` (CS1059) and `-(10 + 5)` prints `-10 + 5`; (3) a constant with no C# literal prints its `ToString()`/type name — `DateTime`, `int[]`, `List<int>`; (4) a block may shadow an outer local, which C# forbids (CS0136), and a block's trailing expression prints as a bare statement (CS0201); (5) `Coalesce` prints `??` on non-nullable operands (CS0019); (6) a lambda callee prints without the brackets it needs (CS0149); (7) `TypeAs` prints `42 as string` (CS0039) although the VM's `as` yields null; (8) `New` of a nested CLR type prints `Outer+Inner` (CS1526); (9) constant `/ 0` is CS0020 while the VM throws at run time; (10) constant overflow is CS0220 while the VM wraps; (11) `PopCount` is writer-refused (IS1c) and `SuspendNode` is erased to its child, so the VM suspends where the printed side returns. |

### Evidence (2026-10-22)

**Corpus.** 128 programs: 114 reconstructed from the 16 test files that call `BuildExpression`
(`ArithmeticNodeTests`, `BlockScopeTests`, `BlockTests`, `CoalesceTests`, `ConstantNodeTests`,
`ForEachLoopTests`, `InterpreterIntegrationTests`, `InterpreterLanguageGotchaTests`,
`LambdaInvokeTests`, `ModuloTests`, `NewNodeTests`, `NumericTypePromotionTests`, `TypeCastTests`,
`UnaryMinusTests`, `MiddlewareInterpreterIntegrationTests`, `ArithmeticParserEvaluatorTests`), plus
14 whose meaning depends on node identity (a local declared once and reused, a shadowing loop
variable) rebuilt with the same instances. `ArithmeticParserEvaluatorTests`' parser is private, so
its 28 expression texts are read with `CSharpLanguage.Read`. `Corpus_CoversEveryTestFileThatBuildsAnExpression`
fails if a file that builds a LINQ expression loses its rows.

**RED, introduced and reverted.** `ParityRow.Agrees` changed to count a rejection on either side as
agreement (the old harness's early return). `dotnet run --project Poly.Tests/Poly.Tests.csproj --
--treenode-filter '/*/*/AstParityTests/*'` → `failed VmRejects_AndPrintedExecutes_IsAFailure`
(`Expected to be false but found True` at `Assert.That(row.Agrees).IsFalse()`),
`failed VmExecutes_AndWriterRejects_IsAFailure`, `failed UnsupportedKinds_AreListedNotSkipped`;
3 failed / 132 succeeded. Reverted → 135 succeeded.

**GREEN.** `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` 0 warnings;
`dotnet run --project Poly.Tests/Poly.Tests.csproj` → 4,473 passed, 0 failed, 0 skipped (4,338
before this slice).

**Not in this slice, and why.** Fixing the 11 classes touches `Poly/Interpretation/CSharp/**`
(writer semantics: literal spelling, precedence, block shadowing, coalesce typing, nested type
names, unchecked arithmetic). This card's Files are two new test files, and each fix changes
emitted output the `EmitGolden` corpus reads. The defects are recorded here and in
`AstParity.KnownDivergences` as evidence for a follow-up card; IS2c (delete the oracle) should not
land until they are fixed, because until then the printed C# is not a faithful second execution.
