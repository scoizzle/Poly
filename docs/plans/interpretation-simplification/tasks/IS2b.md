# TASK IS2b - port the LINQ-oracle test consumers to VM execution

Status: planned. Branch `slice/IS2b`. Lane A (`csharp`). Depends on IS2a.
Plan: [IS-README](../IS-README.md). Audit: [interpretation-simplification.md](../../../analysis/interpretation-simplification.md) §2, stage 2.

## Why

`NodeTestHelpers.BuildExpression` / `BuildExpressionWithParameters` / `CompileLambda` (`Poly.Tests/TestHelpers/NodeTestHelpers.cs:49,61,105`) hand every test a LINQ expression tree built by `LinqExpressionGenerator`. They are the only reason the second interpreter still has consumers. Once IS2a proves the writer→Roslyn differential over the same corpus, this dependence has no job left.

This is the mechanical half of deleting the oracle — deliberately its own PR, because the meaningful risk is a test that quietly stops asserting what it used to. The suite is the oracle for the port: every ported test must assert the same values it asserts today.

## Scope

- Replace every `BuildExpression` / `BuildExpressionWithParameters` / `CompileLambda` call site with one of:
  1. **VM execution** — `Interpreter.Execute(Interpreter.Compile(node))`, asserting the same expected value the test asserts today (this is the default: the tested subject is the node's meaning);
  2. **`AstParity`** (IS2a) — where the test's point is that the printed form agrees with the VM;
  3. **deletion** — where the only assertion was that the LINQ tree had a particular shape. Record why per file: a test whose subject is a deleted implementation is not ported.
- Do not weaken an assertion to make the port easy. If the LINQ tree asserted a CLR type and the VM asserts a `long`, assert the VM's representation explicitly, with a comment naming the representation.
- 139 call sites in 18 files, in this order (largest first, so the pattern settles early):

```
ArithmeticNodeTests 20 · MiddlewareInterpreterIntegrationTests 16 · TypeCastTests 13
InterpreterIntegrationTests 13 · NumericTypePromotionTests 10 · LambdaInvokeTests 9
ConstantNodeTests 9 · UnaryMinusTests 7 · ParameterNodeTests 7 · ForEachLoopTests 6
CoalesceTests 6 · BlockTests 6 · ModuloTests 5 · BlockScopeTests 5 · NewNodeTests 3
InterpreterLanguageGotchaTests 2 · ConditionalTests 2 · ArithmeticParserEvaluatorTests 1
```

- Leave `LinqExpressionGenerator` and `LinqExpressionGeneratorTests` in place. Deleting them is IS2c; this card only removes their consumers.

## Files

Only `Poly.Tests/**`. No production file changes. If a port needs a production change, stop and report — that is a finding, not a permission.

## Shape matrix

| Test kind | Before | After | Test that proves it |
|---|---|---|---|
| "this node evaluates to X" | LINQ tree evaluated | `Interpreter.Execute` result is X | the test itself, with the same expected value |
| "printing agrees with execution" | LINQ vs VM | `AstParity` row | the test itself |
| "the LINQ tree has shape Y" | shape assert | deleted, reason in the Log | suite stays green |

## Done when

- `grep -rn "BuildExpression\|CompileLambda" Poly.Tests/` returns nothing.
- Every ported test asserts a value or a rejection; none becomes assertion-free.
- The full suite is green with the same or a higher test count; every deletion is listed with its reason.
- `LinqExpressionGeneratorTests` and `VmCorrectnessTests` still pass (their own oracle is removed in IS2c).

## SHIP if / NOT SHIP if

SHIP: the grep is empty, the suite is green, and deletions are justified one by one.
NOT SHIP: a test was made vacuous to compile; a production file changed; `LinqExpressionGenerator` was touched.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ArithmeticNodeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/InterpreterIntegrationTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/LambdaInvokeTests/*'
```
Then the full suite, and record the before/after test count.

## Hand-edit

After the port, a node test reads `Compile` then `Execute` — the same two verbs as the code under test.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-22 | agent | Copilot/VS Code | 8887dd3a | **SHIP.** 139 call sites in 18 files ported to **VM execution** (route 1); 0 routed to `AstParity`; 1 test and 11 helper definitions deleted (listed below). `grep -rn "BuildExpression\|CompileLambda" --include=*.cs Poly.Tests/` returns nothing. Full suite **4,502 passed / 0 failed / 0 skipped before, 4,501 / 0 / 0 after** — the one-test drop is the deleted corpus-coverage test, and no other test was removed (4 renames, listed below). `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` 0 warnings; `Poly.Tests` build 0 warnings. Every ported test asserts a value or a rejection; a HEAD-vs-worktree audit compares each test's asserted expectations and explains every difference. No production file changed; `LinqExpressionGenerator` and `LinqExpressionGeneratorTests` untouched. | **The LINQ oracle was encoding its own limits as language meaning.** (1) `Add(Constant(5), Constant("hello"))` — the generator refused the tree, so `IncompatibleTypes_IntPlusString_ThrowsDuringCodeGeneration` asserted a throw; the VM concatenates to `"5hello"`, as C# does, and the test is now `MixedTypes_IntPlusString_ConcatenatesAsCSharpDoes`. (2) `TypeAs(object 42 → int)` — the LINQ tree lifted it to `int?` 42; the VM fails closed at compile, so `TypeAs_ObjectToNonNullableValueType_ReturnsNullableValue` becomes `..._RejectsAtCompile` and pins the rejection. (3) Three assertions (2 `ReferenceEquals` pairs and the `BinaryExpression` cast) asserted parameter-expression identity — a property of the generator's plumbing, not of node meaning — and are replaced by value assertions (`Add(x, x)` reads the one argument). (4) `AstParityTests.Corpus_CoversEveryTestFileThatBuildsAnExpression` was a search for the deleted call pattern; with no file left to find it is vacuous, so it is deleted (route 3) together with the `RepoRoot` helper only it used. |

### Port routes and deletions (2026-10-22)

Route 1, VM execution (`Interpreter.Execute(Interpreter.Compile(node))`, same expected values as before), per file:
`ArithmeticNodeTests` 20 · `MiddlewareInterpreterIntegrationTests` 16 · `TypeCastTests` 13 ·
`InterpreterIntegrationTests` 13 · `NumericTypePromotionTests` 10 · `LambdaInvokeTests` 9 ·
`ConstantNodeTests` 9 · `UnaryMinusTests` 7 · `ParameterNodeTests` 7 · `ForEachLoopTests` 6 ·
`CoalesceTests` 6 · `BlockTests` 6 · `ModuloTests` 5 · `BlockScopeTests` 5 · `NewNodeTests` 3 ·
`InterpreterLanguageGotchaTests` 2 (the `AssertLinq*` helpers) · `ConditionalTests` 2 ·
`ArithmeticParserEvaluatorTests` 1 (`EvaluateWithLinq` → `Interpreter`). 139 total.

Route 2, `AstParity`: none. The differential already covers these programs as corpus rows; a
second caller of it would duplicate the IS2a corpus rather than prove these nodes.

Route 3, deletions, each with its reason:

| Deleted | Why |
|---|---|
| `AstParityTests.Corpus_CoversEveryTestFileThatBuildsAnExpression` (+ the `RepoRoot` helper only it used) | Its subject is "which test files call the deleted oracle". After the port no file calls it, so both halves of the assertion are vacuous. |
| `ForEachLoop_LoopVariableDoesNotLeakOutsideLoopScope`: the `BuildExpression()` throw assertion | The same `InvalidOperationException` is asserted one statement later through `Interpreter.Compile`. |
| `TypeAs_ObjectToNonNullableValueType_*`: the LINQ `42` assertion | The VM refuses the program; the LINQ lift cannot be asserted on the shipped meaning. Replaced by the rejection assertion, see finding 2. |
| `MixedTypes_IntPlusString_*`: the LINQ `Throws<Exception>` assertion | The VM executes it; replaced by the concatenation value, see finding 1. |
| `NodeTestHelpers.BuildExpression`, `BuildExpressionWithParameters`, `CompileLambda`, `AssertDualOracleInt`, `AssertDualOracleDouble` | The adapters themselves. Nothing calls them after the port; IS2c authorised removing them "if IS2b left them in place". `AnalyzeNode`, `UseAllAnalyzers`, `CompileWithPrimitives`, `ExecWithPrimitives` stay. |
| `InterpreterLanguageGotchaTests.AssertLinqLong` / `AssertLinqBool` and their 5 call sites | Each call duplicated the VM assertion on the next line of the same test with the same expected value (`false`/`0L`, `true`/`1L`, `-3L`, overflow `expected`). |

Strengthened, not weakened: `TypeCast_NonNullableToNullable_WorksCorrectly` asserted only
`Result.HasValue`; it now asserts the value, `42`. The VM represents an `int` as a `long`, so the
integer assertions name the representation they read (`GetValue<long>()`); `GetValue<int>()` is used
where the node's own type is `int` (`int?` → `int` reads the stored `long`).

Renamed (same test, true meaning): `Parameter_SameParameterTwice_ReturnsSameExpression` →
`..._UsesSameArgument`; `ContextPreservation_MultipleParameterAccess_UsesSameParameterExpression` →
`..._UsesSameValue`; `IncompatibleTypes_IntPlusString_ThrowsDuringCodeGeneration` →
`MixedTypes_IntPlusString_ConcatenatesAsCSharpDoes`; `TypeAs_ObjectToNonNullableValueType_ReturnsNullableValue`
→ `..._RejectsAtCompile`.
