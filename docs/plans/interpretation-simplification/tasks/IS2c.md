# TASK IS2c - delete the second interpreter

Status: **in flight** — the deletion is complete on `slice/IS2c`; the C99 harness it blocked on was deleted by Scot's 2026-10-22 decision (see Log). Dependencies IS2b and IS2d are merged. Branch `slice/IS2c`. Lane A (`csharp`).
Plan: [IS-README](../IS-README.md). Audit: [interpretation-simplification.md](../../../analysis/interpretation-simplification.md) §2.

## Held until the writer agrees with the VM

IS2a built the replacement differential and found 29 rows where the printed program disagrees with the VM — 11 writer defect classes, two of them silent wrong values ([#279](https://github.com/scoizzle/Poly/issues/279)). Those rows are pinned in `AstParity.KnownDivergences`, so **nothing verifies VM behaviour for those shapes today**. Deleting the LINQ oracle first would delete the only oracle for them as well.

This slice therefore starts when [IS2d](IS2d.md) has emptied `KnownDivergences`. That is the card's own precondition, restated as a dependency, not a new one.

## Why

`LinqExpressionGenerator` is 1,093 LOC in three files (`Poly/Interpretation/LinqExpressions/`) with **zero production references** — the only mention outside its own folder is a doc comment in `DomainExpressionLoweringPass.cs:23`. As an oracle it is unsound in both directions: it has fewer arms than the VM, it accepts `ParameterReference` that the VM rejects, it models `this` as `null`, and its harness skips exactly the interesting failure.

After IS2a there is a differential with an external authority (Roslyn) and after IS2b nothing depends on the in-house one. Deleting it removes one interpreter, one hand-kept switch, and the question "which of two interpreters is right?"

## Scope

- Delete `Poly/Interpretation/LinqExpressions/` (3 files, 1,093 LOC) and `Poly.Tests/Interpretation/LinqExpressionGeneratorTests.cs`.
- **`VmCorrectnessTests` is not ported — it is replaced.** Its 64 tests compare the VM to the LINQ oracle and assert **no independent expected value** (`await Assert.That(vmVal).IsEqualTo(linqVal)`). They cannot be ported; converting them to explicit expectations is a separate, larger piece of work. Delete the parity-only cases and show, in the Log, that every expression shape they covered has an `AstParity` (IS2a) row. If a case asserts something the parity harness does not cover, it moves to `AstParity` first — deleting coverage is not allowed.
- Remove `Poly.Tests/TestHelpers/NodeTestHelpers.cs`'s `BuildExpression*`/`CompileLambda` helpers if IS2b left them in place, and any `using Poly.Interpretation.LinqExpressions;`.
- Rewrite `Poly/Interpretation/README.md:9` — it currently presents the LINQ path as a semantic checker — and delete its entry at `:257`.
- Update the `DomainExpressionLoweringPass.cs:23` comment.
- Record the closure: issue evidence, the `-1,093 LOC` figure, and the parity coverage mapping.

## Files

- `Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.cs`, `.Helpers.cs`, `.ControlFlow.cs` — deleted
- `Poly.Tests/Interpretation/LinqExpressionGeneratorTests.cs` — deleted
- `Poly.Tests/Interpretation/VmCorrectnessTests.cs` — parity-only cases removed, coverage mapped
- `Poly.Tests/TestHelpers/NodeTestHelpers.cs`
- `Poly/Interpretation/README.md`
- `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` — comment only

## Shape matrix

| Item | Before | After | Test that proves it |
|---|---|---|---|
| `LinqExpressionGenerator` | 1,093 LOC, 33 test references | absent | `grep -r LinqExpressionGenerator` returns only this plan |
| `VmCorrectnessTests` parity cases | VM == LINQ | deleted, shapes covered by `AstParity` | the coverage mapping in the Log + `AstParityTests` green |
| an expression shape only LINQ covered | covered by two in-house interpreters | covered by VM + Roslyn | one `AstParity` row per shape |
| the interpreter README | documents a second interpreter | one interpreter plus Roslyn | doc read |

## Done when

- The folder, the test class, and every reference are gone.
- The coverage mapping is complete: no expression shape lost an assertion.
- `Poly/Interpretation/README.md` describes the pipeline that exists.
- Full suite green; the test count change is explained by the deleted parity cases.

## SHIP if / NOT SHIP if

SHIP: the deletion is total, the coverage mapping shows no lost shape, and the README matches the code.
NOT SHIP: a reference survives behind `#if`; a `VmCorrectnessTests` case is deleted without a mapped replacement; the README still mentions the LINQ path.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmCorrectnessTests/*'
```
Then the full suite.

## Hand-edit

There is one interpreter. Its oracle is a compiler in another package, named where the parity tests live.

## Needs Scot

**Carried out, and Scot decided the one call the card left open.** The parity cases are deleted rather than converted to explicit expected values: they had no independent expectation, so a "port" would invent one per case. The count is **17**, not the card's 64 — the card counted every case in `VmCorrectnessTests` as parity, while 90 of its 107 asserted an expected value independently and were never touched. The 11 shapes that had no `AstParity` row were moved there first; the rest were already covered. Scot's 2026-10-22 decision also deleted `C99ParserInterpreterTests`, which this card had not scoped and could not port — the Log records the evidence and the replacement ([IS2e](IS2e.md)). If Scot wants explicit-value versions of the 17, that becomes a follow-up card.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-22 | agent | Copilot/VS Code | 1a2699fc | **BLOCKED.** Deleted `Poly/Interpretation/LinqExpressions/` (3 files, 1,093 lines) and `LinqExpressionGeneratorTests` (9 tests); `VmCorrectnessTests` 107 → 90 (17 parity-only cases out, 3 kept and renamed); `AstParityTests` 134 → 145 (11 shapes moved in **before** anything was deleted); `Poly/Interpretation/README.md` and the `DomainExpressionLoweringPass.cs:23` comment now describe the pipeline that exists. `Poly` and `Poly.Benchmarks` build with 0 warnings. Full suite with `C99ParserInterpreterTests.cs` set aside: **4,459 total / 4,458 passed**; the one failure is `RelativeLinks_UnderDocsAndGithub_Resolve`, naming the file the run had moved. With it restored the test project is red on that one file (2 errors), so nothing was pushed and no PR was opened. | The card's premise "after IS2b nothing depends on the in-house one" is **false**: four files still constructed the generator directly, because IS2b ported only the `BuildExpression`/`CompileLambda` call sites. Three were mechanical and are closed here. The fourth is structural — see *The C99 harness: deleted by decision*. No new writer defect: all 11 moved shapes agreed with Roslyn on the first run. |
| 2026-10-22 | agent | Copilot/VS Code | 6a12cd16 | **SHIP.** The blocker is resolved by decision, not by a port: `C99ParserInterpreterTests.cs` is deleted (26 tests; 19 executed a parsed program, 7 asserted parse rejections). Full suite **4,459 passed / 0 failed / 0 skipped**; `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` 0 warnings; `grep -rn "LinqExpressionGenerator" --include=*.cs Poly/ Poly.Tests/` returns nothing. The claim the harness carried is not buried: the trust-proof map's §3.1 row is now **Yellow — evidence withdrawn 2026-10-22**, the dual-oracle clause is removed from the row above it, and [IS2e](IS2e.md) carries the replacement. | **Why the file could not survive:** its parser models locals as assignable `Parameter` nodes, and the VM refuses that by design — Scot's independent probe, `Block([Assignment(Parameter, 0L), Assignment(Parameter, Parameter + 5L), Parameter])`, fails with *"VM compile rejected: assignment destination must be a Variable, Member, or IndexAccess, got Parameter"* (`AbiEmitter.EmitAssignment`, `AbiEmitter.Statements.cs:86`). Rescue meant changing VM assignment semantics or giving `Variable` a declared type — new language surface driven by a test-only, non-shipped language (`AGENTS.md` tenet 5). The 19 execution tests were green only because the deleted oracle was *more permissive* than the VM, so they were never evidence about Poly. The deletion cost a documented proof; IS2e is that cost, booked. |

### Coverage mapping — the 17 deleted parity cases

Every case below asserted `await Assert.That(vmVal).IsEqualTo(linqVal)` and nothing else, so it had no expected
value to port. One row per case: the shape it exercised and where that shape is asserted now. Nothing was deleted
before its replacement existed: the eleven `ParityShapes` rows were added to `AstParityTests` and were green
(`AstParityTests` 134 → 145) before the folder was removed.

| Deleted case | Shape it exercised | Asserted now by |
|---|---|---|
| `InstanceMethod_InvokeMember_Triple_DualOracle` | `Invoke(Member(instanceParameter, "Triple"))` with a seeded object | moved to `AstParity` as `ParityShapes.InstanceMethodNoArguments` |
| `InstanceMethod_InvokeMember_WithArg_DualOracle` | instance method taking one argument | moved to `AstParity` as `ParityShapes.InstanceMethodWithArgument` |
| `Regression_DynamicInvoke_ParameterMismatch_ArithmeticOnly` | `Add(literal, literal)` through the domain lowering pass, five operand pairs | `ArithmeticNodeTests.Add_TwoIntegers_ReturnsSum` (corpus) |
| `MatchLinq_Literal` | `Constant(42)` as the program result | `ConstantNodeTests.Constant_IntegerValue_CompilesAndExecutes` (corpus) |
| `MatchLinq_NegativeLiteral` | negative literal as the program result | moved to `AstParity` as `ParityShapes.NegativeLiteral` |
| `MatchLinq_Add` | `Add(1, 2)` at the root | `ArithmeticNodeTests.Add_TwoIntegers_ReturnsSum` (corpus) |
| `MatchLinq_DeepArithmetic` | `Add(Multiply(Add(2,3), 4), 10)` | `ArithmeticNodeTests.NestedOperations_AddAndMultiply_EvaluatesCorrectly` (corpus) |
| `MatchLinq_Comparisons` | `GreaterThan`, `LessThan`, `Equal`, `NotEqual` as values | `LessThan`/`Equal`/`NotEqual`: `InterpreterLanguageGotchaTests.LessThan_NaN_IsFalse`, `Equal_NaN_IsFalse`, `NotEqual_NaN_IsTrue` (corpus); `GreaterThan`: moved to `AstParity` as `ParityShapes.GreaterThan` |
| `MatchLinq_AndOr` | `And` and `Or` of comparisons as values | moved to `AstParity` as `ParityShapes.LogicalAnd`, `ParityShapes.LogicalOr` |
| `MatchLinq_Not` | `Not(Equal(...))` as a value | moved to `AstParity` as `ParityShapes.LogicalNot` |
| `MatchLinq_RandomArithmetic` | 20 random Add/Subtract/Multiply/Divide/Equal trees over literals | `ArithmeticNodeTests.*` and `InterpreterIntegrationTests.ComplexExpression_MultipleNestedOperations_EvaluatesCorrectly` (corpus) |
| `MatchLinq_PropertyAccess_Age` | property read on an entity parameter | moved to `AstParity` as `ParityShapes.PropertyRead` |
| `MatchLinq_MethodCall_StringLength` | `Member(Constant("hello"), "Length")` | moved to `AstParity` as `ParityShapes.StringLength` |
| `MatchLinq_Coalesce_NonNull` | `Coalesce("hello", "world")` — non-null left, string operands | moved to `AstParity` as `ParityShapes.CoalesceNonNullLeft` (the old case only asserted "not the zero handle"; the row asserts the value) |
| `MatchLinq_PropertyAccess_NameEq` | `Equal(Member(parameter, "Name"), "Alice")` | moved to `AstParity` as `ParityShapes.PropertyReadAndStringEquality` |
| `MatchLinq_Lambda_NoCapture` | `Invoke(Lambda([], 42))` | `LambdaInvokeTests.Lambda_WithNoParameters_CompilesAndReturnsConstant` (corpus) |
| `Fuzz_RandomPropertyAccess_MatchLinq` | property read on a parameter, three subject variants | moved to `AstParity` as `ParityShapes.PropertyRead` |

**Kept, because they assert VM behaviour independently of the oracle** (LINQ scaffolding removed, values unchanged):
`MatchLinq_StringEquality` → `StringEquality_OnEntityProperty_ReturnsOneOrZero`; `MatchLinq_CompositeGuard` →
`CompositeGuard_AgeInRange_ReturnsOneOrZero` (its single-caller helper inlined);
`Fuzz_CallExternal_MultipleRefParams` (kept as-is; its stale "LINQ Expression" comment is the only edit).

**Deleted with the implementation** — `LinqExpressionGeneratorTests`, 9 tests whose subject is the generator's own
bookkeeping, not language meaning: `Compile_ReusedGenerator_DoesNotLeakParametersBetweenRootCompilations`,
`..._DoesNotLeakTypedReturnLabelsBetweenRootCompilations`, `Compile_NodeWithNestedLambda_ExportsOnlyOuterParameters`,
`..._WithConstantFolding_UsesFoldedConstantReplacement`, `..._InvokeLambdaWithArguments...`,
`..._AddWithZeroRight...`, `..._AndWithConstantTrueLeft...`, `Compile_Variable_FirstAssignmentIsTheWrite`,
`Compile_Variable_LaterAssignmentDoesNotReplayFirstWrite`.

## Count reconciliation

`4,501` before → **`4,459` after**, 0 failed, 0 skipped. Nothing unexplained:

| Change | Tests |
|---|---|
| `LinqExpressionGeneratorTests` deleted with the implementation | −9 |
| `VmCorrectnessTests` parity-only cases deleted (mapping above) | −17 |
| `VmCorrectnessTests` cases kept, renamed, values unchanged | 0 |
| `DomainExpressionVmExecutionTests.Policy_LinqAndVmPaths_Agree` (pure dual-engine parity) deleted | −1 |
| `LanguageVmTests.Await_TaskLong_LinqCheckerCompiles` → `..._VmExecutes` (same `42`) | 0 |
| `AstParityTests` `ParityShapes` rows added **before** the deletion | +11 |
| `C99ParserInterpreterTests` deleted (26th of them a dual-run that measured a non-shipped language) | −26 |
| **Net** | **−42** |

`4,501 − 42 = 4,459`. A run with the C99 file merely set aside also reported `4,459`, confirming the file contributed
26 tests and no other file changed between the two runs.

## LOC delta

The program's accounting claims **−1,093** for this slice; that is the `wc -l` count of the three deleted production
files (`git` records 1,096 deleted lines because none of them ends with a newline). **Actual whole-slice delta, all
files: +59 / −3,166 = −3,107 lines.**

Production `.cs`: **−1,096** — the three generators (500 + 305 + 291) plus the two-line comment rewrite in
`DomainExpressionLoweringPass.cs`.

Test `.cs`: **−2,011** —

| File | + | − |
|---|---|---|
| `C99ParserInterpreterTests.cs` (deleted) | 0 | 1,407 |
| `LinqExpressionGeneratorTests.cs` (deleted) | 0 | 192 |
| `VmCorrectnessTests.cs` | 3 | 353 |
| `DomainExpressionVmExecutionTests.cs` | 4 | 65 |
| `PolicyEvaluator.cs` | 0 | 42 |
| `LanguageVmTests.cs` | 3 | 5 |
| `AstParityTests.cs` | 43 | 0 |
| `AstParity.cs` (doc comment) | 3 | 3 |
| `MiddlewareInterpreterIntegrationTests.cs` (doc comment) | 1 | 1 |

## Files outside lane A

Authorised by Scot on 2026-10-22. A deletion that stops the test project compiling has to close its consumers; the
alternative is that nothing lands.

| File | One-line reason |
|---|---|
| `Poly.Tests/TestHelpers/PolicyEvaluator.cs` | `CompileLinqPredicate` and `EvaluateWithDualOracle` existed only to run the oracle; deleted, the VM predicate stays |
| `Poly.Tests/DomainModeling/Lowering/DomainExpressionVmExecutionTests.cs` | dead `ExecuteViaLinq`/`ExecuteDomainViaLinq`; one predicate built on the oracle; the pure dual-engine parity test `Policy_LinqAndVmPaths_Agree` |
| `Poly.Tests/Interpretation/LanguageVmTests.cs` | lane C: `Await_TaskLong_LinqCheckerCompiles` ported to the VM as `Await_TaskLong_VmExecutes`, same `42` |
| `Poly.Tests/Integration/MiddlewareInterpreterIntegrationTests.cs` | a doc comment naming the generator now says "the VM" |
| `Poly.Tests/Integration/C99ParserInterpreterTests.cs` | deleted by decision — see below |
| `docs/plans/archive/completed-2026-08-mid/{gi-preflight-c99-notes,grammar-integration}.md` | two live links to the deleted file became code spans; the relative-link test fails otherwise |
| `docs/plans/reference/customer-trust-proof-map.md` | the §3.1 rows the deletion invalidated |

## The C99 harness: deleted by decision

The file was the last consumer of the deleted class, and it could not be ported. Four substrates were tried; each hit
the same wall:

1. **VM via a typed delegate** — `Assignment` to a `Parameter` is refused by design (19 of 26 fail).
2. **VM via `Invoke(Lambda(params, body))`** — same rejection; the parser declares locals as `Parameter` nodes on
   `Block.Variables`.
3. **Printer + Roslyn** — the entry point prints as `void Run(...) => a + b;` for a body ending in a valued `Return`
   (CS0201); folding the trailing `Return` into the block's value leaves 15 failing because the writer does not declare
   a `Parameter` block variable, so `result = 1;` prints with no `result` in scope.
4. **Locals as `Variable`** — the VM-accepted shape, but `Variable` carries no declared type, so `double total = 0;`
   would infer `int`: the silent-wrong-value class this program exists to remove.

The shipped shape is `Variable` for writes and `Parameter` for read-only bindings; the C99 parser needs a *typed,
assignable* local, which is language surface Poly does not have and should not grow for a test-only language
(`AGENTS.md` tenet 5; the `DomainEntityInstance` precedent).

**What the deletion costs, and where it is booked.** The harness was the instrument for the trust-proof row
*"Grammar structure dispatch matches RD handlers"*. That row is now **Yellow — evidence withdrawn 2026-10-22**, kept in
place with the reason and a pointer to [IS2e](IS2e.md); the *"Dual path ⇒ dual agreement"* row lost its
"Policy VM-primary + LINQ reference" dual and its "C99 GIP `DualRun_*`" clause, with the same date recorded. No
substitute evidence was invented here — [IS2e](IS2e.md) is the work that rebuilds an instrument on a shipped grammar,
and it opens with the question of whether the claim is worth proving at all (tenet 7).

**Premise correction, for the record.** This card's premise was wrong twice: not only was "after IS2b nothing depends
on the in-house one" false, but so was the implicit "`BuildExpression`/`CompileLambda` were the only entry points" —
four files constructed `LinqExpressionGenerator` directly. IS2b ported the call sites it hashed, and the grep that
closed it did not cover direct construction.
