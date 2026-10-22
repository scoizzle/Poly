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
