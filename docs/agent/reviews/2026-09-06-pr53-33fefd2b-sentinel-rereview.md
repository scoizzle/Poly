# Sentinel re-verify PR 53 — 2026-09-06

- **Target**: PR 53 https://github.com/scoizzle/Poly/pull/53 · SHA `33fefd2b95b592ab7b4a02cf588e511206f412a0` · branch `cleanup/interpretation-coverage` (worktree `review/sentinel-pr53-33fefd2b`)
- **Mode**: re-verify
- **Issue counts**: 3 bugs, 2 suggestions, 0 nits (open after this pass)
- **Verdict**: not ship as F1–F23 closed. Test-only coverage is real for F4–F11 / F13–F20 / F22–F23. Product F1 / F2 / F3 / F12 remain; F21 escaped-set oracle is still missing.
- **Process notes**: Nested `2724/0` is not the Interpretation `[Test]` count. Primary greps this session: Interpretation **928** `[Test` attributes (merge-base `a6735beb` **827**, +101); **62** files under `Poly.Tests/Interpretation/`; all `Poly.Tests` `[Test` **2703**. PR diff is test+docs only (`git diff --name-only a6735beb..HEAD -- 'Poly/**/*.cs'` empty). Characterization tests that lock current product bugs do not close those bugs. Optional TUnit `--filter FullyQualifiedName~Interpretation` is invalid on this host; `--treenode-filter` attempts ran 0 tests — suite not re-executed this session.

## Summary

PR 53 at `33fefd2b` is Nested’s test-only close of Sentinel F1–F23 against master `a6735beb`. Most named test obligations now exist and exercise the requested path (Compile+Execute, diagnostic code, or honest fail-loud). Four product hooks Nested themselves listed are **still true on this SHA**: `EmitThrow` discards non-`New` operands; `Resume` re-enters `SuspendNode`; unmatched `Member` invoke Compile-accepts; catch `VariableName` is undeclared at analysis. Sibling-path check: LINQ throw **preserves** the operand and LINQ catch **binds** the name, while the canonical VM does neither. Do not treat Nested “closed (current behavior)” as a product fix.

## Counts (primary evidence this session)

| Item | Count | Evidence |
|------|------:|----------|
| HEAD SHA | `33fefd2b95b592ab7b4a02cf588e511206f412a0` | `git rev-parse HEAD`; matches `gh pr view 53` `headRefOid` |
| `Poly/**/*.cs` in PR | 0 | `git diff --name-only a6735beb..HEAD -- 'Poly/**/*.cs'` |
| `Poly.Tests/Interpretation/*.cs` | 62 | `find` |
| Interpretation `[Test` (incl. `[Test, Timeout`) | **928** | `rg -c '\[Test'` sum |
| merge-base Interpretation `[Test` | **827** | `git grep -c '\[Test' a6735beb` sum |
| All `Poly.Tests` `[Test` | **2703** | `rg -c '\[Test' Poly.Tests` (Nested 2724 unreproduced) |
| Interpretation `[Test]` exact (misses Timeout) | 836 | `rg -c '^\s*\[Test\]'` — do not use this number |

## Checklist

- [x] Diff collected; test-only scope (no product `.cs`)
- [x] Stance: adversarial re-verify; Nested chat not trusted
- [x] Sibling-path: VM vs LINQ throw; VM vs LINQ catch bind; Member invoke analyze vs emit
- [x] Reachability before severity on F1/F2/F3/F12
- [x] F1–F23 dispositioned from current source + tests
- [x] Counts recomputed; 2724 rejected
- [x] Review + follow-ups written (do not overwrite 2026-09-06-interpretation-coverage-sentinel*.md)

---

## F1–F23 dispositions

| F# | Disposition | Evidence (this session) |
|----|-------------|-------------------------|
| **F1** | **still open** (product bug; tests characterize discard) | `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:324-340` still compiles non-`New` then `Throw(New(typeof(Exception)))`. Tests: `ThrowVmTests.cs:45-82` assert **not** same instance / `typeof(Exception)`. LINQ sibling **preserves** operand (`LinqExpressionGenerator.cs:268`). Reachable on legal `ThrowStatement(Constant(ex))` / `Variable`. |
| **F2** | **still open** (product bug; fail-loud **asserted**) | `VariableLifetimePass.cs:70-108` has no `CatchClause` arm (foreach `CatchClause` in that file = 0). Emitter allocates a **new** `Variable` (`Statements.cs:353-370`). Test `ExceptionHandlingVmTests.cs:123-143` Compile-throws `"not declared"`. LINQ sibling **binds** the name (`LinqExpressionGenerator.ControlFlow.cs:427-440`). Canonical VM catch-var still unusable. |
| **F3** | **still open** (product bug; tests characterize re-suspend) | `EmitSuspendNode` places `Label(resumeLabel)` **before** inner + suspend + `Goto(Exit)` (`Statements.cs:387-409`). `Interpreter.Resume` sets `Resuming` then re-invokes the delegate (`Interpreter.cs:142-147`). Tests: `SuspendResumeVmTests.cs:17-27`, `:34-48` assert `resumed.IsSuspended`. Reachable on valid `Block` with stmts after `SuspendNode`. |
| **F4** | **fixed** (test) | `LanguageSurfaceTests.cs:137` sample + `:182-185` dedicated compile-reject. |
| **F5** | **fixed** (test) | `LanguageSurfaceTests.cs:165` constructs `ResolvedTypeReference`. |
| **F6** | **fixed** (test) | `InvalidProgramTests.cs:71-91` pins JT0005 + CF0001 sibling + JT0003; `JumpTargetAnalysisTests.cs:55-60` JT0003. |
| **F7** | **fixed** (test) | Rename `If_ConstFalse_ElidesThenBranch` (`ControlFlowAnalysisTests.cs:350`); `MustExecuteMetadata` (`:402-423`); CF0001/4/6/10/13 (`:427-476`). |
| **F8** | **fixed** (test) | `SideEffectAnalysisTests.cs:48-55` enables `EmitElisionDiagnostics` → `DEAD_CODE_ELIDABLE`; metadata tests `:17-88`. |
| **F9** | **fixed** (test) | `DefiniteAssignmentTests.cs:75-83` stamps `DefiniteAssignmentMetadata`; if-merge `:45-72`; loop non-leak `:86-96`. |
| **F10** | **fixed** (test) | `LambdaReturnTypeAnalyzerTests.cs:18-45` Invoke(Lambda) + stored Invoke(Variable) body types. (Lambda-node type assert is skippable if null — not reopened.) |
| **F11** | **fixed** (test) | `ConstantFoldingTests.cs:517-537` `GetNodeReplacement` is `Constant(5)` + `Interpreter.Compile`+Execute yields 5. Emitter consumes replacements (`DirectVmAbiEmitter.cs:160-163`). |
| **F12** | **still open** (product suggestion; IndexOf(char) VM oracle **fixed**) | `CompileExecute_IndexOfChar_ReturnsVmIndex` (`MethodInvocationSemanticResolutionTests.cs:50-53`). Unmatched `Substring(1.5)`: `Interpreter.Compile` **accepts** (`:65-71`). Cause: `SyntaxTypeCompatibilityAnalyzer.CheckInvokeTarget` returns immediately on `Member` (`:199-201`) with no resolved-member requirement. `Equals(string)` used instead of `IndexOf(string)`; Nested `-1` claim **not** independently executed. |
| **F13** | **fixed** (test) | `TypeDefinitionNodeAnalyzerTests.cs:312-329` Optional/Map property runtime types. |
| **F14** | **fixed** (test) | `AstConstructorDefinitionTests.cs:78-89` New(AST) Compile fail-loud `"no matching constructor"`; `AstMemberVmTests.cs:23-51` Member/Assignment via `Interpreter.Compile`. |
| **F15** | **fixed** (test) | `InvokeMemberInstanceTests.cs:140-159` AST method / no CLR host fail-loud `"does not define method"`. |
| **F16** | **fixed** (test) | `CSharpGeneratorTests.cs:841-879` Comment/Default/TypeOf/NewArray/Suspend/PopCount/StridedSetBits/NullForgiving/BitwiseNot/Await + Map/Optional; Union = Generate fallback (`CSharpGenerator.cs:1246-1248` `ToString()`, no `UnionTypeReference` arm). |
| **F17** | **fixed** (test) | `UsingStatementVmTests.cs:8-14` non-IDisposable skip; `:17-25` nested using; `ForEachEnumeratorDisposeTests.cs:9-46` Dispose after complete/break/throw. Product skip is `IfThen(TypeIs IDisposable)` (`Statements.cs:650-667`, `:631-635`). |
| **F18** | **fixed** (test) | `ExceptionHandlingVmTests.cs:147-171` inner catch; `:175-199` throw-in-catch → outer. |
| **F19** | **fixed** (test) | `InterpretResultAbiTests.cs:53-85` Break/Continue/Throw are not `ResultKind.Break/Continue/Throw`. |
| **F20** | **fixed** (test) | Theater replacements: `CallSiteCatalogTests.cs:35-43` renamed + `ResolvedInvoke_GetsSiteIndex` `:56-68`; CF then-elision rename; `TypeCastTests.cs:143-148` `typeof(double)`; `BlockTests.cs:118-123` last-expr `int`; `Member_OnNull_CompileRejects` (`InterpreterLanguageGotchaTests.cs:478-482`); TH0002 dropped (`ThisReferenceTests.cs:150-161`); `PropertyDefinitionNodeTests.cs:22-34` Analyze. |
| **F21** | **still open** (suggestion: escaped set) | `VariableScopeTests.cs:17-54` metadata + shadow + `CapturedVariables`. `EscapedVariables` has **zero** Interpretation test hits (`rg EscapedVariables Poly.Tests/Interpretation` empty). Pass still computes it (`VariableLifetimePass.cs:11,46,186-209`). |
| **F22** | **fixed** (test) | `VmHeapComparisonTests.cs:8-38` DateTime/DateOnly/Guid; mixed DateOnly/string compile-rejects (earlier than `VmHeapComparison.cs:19-21` runtime throw). `VmHeapRelationalTests.cs` additional DateOnly/string order. |
| **F23** | **fixed** (test) | `MermaidAstGeneratorTests.cs:12-49` executable Add/If + AnalysisOnly `TypeDefinitionNode`. |

---

## Issues still open

### Issue 1 -- Severity: bug (F1)
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:324-340`
- Description: `EmitThrow` keeps the operand only when `ts.Exception is New`. Any other operand is compiled for side effects then replaced with `throw new Exception()`. `ThrowExpression` shares this arm (`DirectVmAbiEmitter.cs:217`). Tests at `ThrowVmTests.cs:45-82` **lock the discard in**. LINQ sibling throws the compiled operand (`LinqExpressionGenerator.cs:268`) — sibling-path drift. Reachability: legal Syntax (`Constant` / `Variable` holding an exception); LanguageVm `ThrowExpression_ThrowsOperand` (`LanguageVmTests.cs:158`) still only constructs `New`.
- Suggestion: Preserve the heap object for non-`New` operands (same instance). Flip the three characterization tests to same-instance oracles. Do not leave LINQ correct and VM wrong.
- Status: open

### Issue 2 -- Severity: bug (F2)
- File: `Poly/Interpretation/Analysis/Semantics/VariableLifetimePass.cs:70-108`; `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:353-370`
- Description: `CatchClause.VariableName` is a string. ScopeValidator never registers it (no `CatchClause` arm; children walked as undeclared `Variable`). Emitter’s synthetic `new Variable(clause.VariableName)` is a different instance under `ReferenceEqualityComparer`. Nested asserted Compile reject `"not declared"` (`ExceptionHandlingVmTests.cs:140-143`) — honest current VM, **not** a bind. LINQ binds the name via `DeclareParameter` (`LinqExpressionGenerator.ControlFlow.cs:437-440`). Catch-var surface remains unusable on the canonical VM.
- Suggestion: ScopeValidator must declare the catch binding; emitter must write that same instance (or the analysis-declared one). Keep a fail-loud test until bind works, then assert message/type.
- Status: open

### Issue 3 -- Severity: bug (F3)
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:387-409`; `Poly/Interpretation/Interpreter.cs:142-147`
- Description: Resume PC dispatch jumps to a label at the **start** of `SuspendNode`, which re-runs inner, sets `Suspended`, and exits again. `SuspendResumeVmTests.cs:34-48` asserts re-suspend with no fall-through to later assignments. `Resume_WhenNotSuspended_Throws` (`:8-14`) is the only closed slice of F3. Reachability: valid program with statements after `SuspendNode`.
- Suggestion: Place the resume label after the suspend/exit so remaining statements run; assert `x==2` after resume in that block. Do not treat `resumed != null` (`:23-24`) as continuation coverage.
- Status: open

### Issue 4 -- Severity: suggestion (F12)
- File: `Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs:199-201`; `Poly.Tests/Interpretation/MethodInvocationSemanticResolutionTests.cs:65-71`
- Description: `CheckInvokeTarget` returns immediately when `invoke.Delegate is Member` — unmatched overloads produce no Error. `Interpreter.Compile(Substring(1.5))` accepts (`:70-71`). Emit then falls through to `InvokeNamed` (`DirectVmAbiEmitter.Invoke.cs:108-110`) — fail-loud only at execute, if at all. IndexOf(char) VM oracle is in place (`:50-53`). `IndexOf(string)` VM result was not asserted this SHA.
- Suggestion: Analyze-time reject when `GetResolvedMember` is null for a Member invoke; Compile-reject the Substring(1.5) tree. Optionally add IndexOf(string) Compile+Execute (do not trust Nested’s `-1` note without a test).
- Status: open

### Issue 5 -- Severity: suggestion (F21 leftover)
- File: `Poly/Interpretation/Analysis/Semantics/VariableLifetimePass.cs:11,186-209`; `Poly.Tests/Interpretation/VariableScopeTests.cs` (no escaped assert)
- Description: F21 asked captured **and** escaped sets used by emit. Captured is asserted (`VariableScopeTests.cs:42-54`). `EscapedVariables` is untested under Interpretation.
- Suggestion: One test that marks a variable escaped (invoke arg / return / foreach collection) and asserts `VariableAnalysisMetadata.EscapedVariables`.
- Status: open
