# Sentinel re-verify PR 53 — 2026-09-06

- **Target**: PR 53 https://github.com/scoizzle/Poly/pull/53 · PINNED SHA `ad6b427f4d739204bb184dbf630ed83cf1802fda` · worktree branch `review/sentinel-pr53-ad6b427f`
- **Mode**: re-verify
- **Issue counts**: 0 bugs, 1 suggestion, 0 nits (open after this pass)
- **Verdict**: ship as F1 F2 F3 F12 F21 product-closed. Prior 33fefd2b not-ship does not apply to this SHA. Residual F24 is name-fallback catch bind under same-name outer register (untested shadow); does not reopen F2’s original undeclared/Compile-reject hook.
- **Process notes**: Nested “PRODUCT close” is real on this SHA (6 product `.cs` files vs `a6735beb`; empty product diff at `33fefd2b`). Do not chain-trust Nested chat or the 33fefd2b follow-ups `[x]` marks — dispositions below are from current source + tests this session. Requested `--filter FullyQualifiedName~Interpretation` is invalid (TUnit/MTP: `Unknown option '--filter'`). `--treenode-filter` matched 0 tests. Full suite executed instead: **2726/0**. Interpretation `[Test` attribute count is **930**, not 2726.

## Summary

`ad6b427f` is the product pass after Sentinel not-ship on `33fefd2b`. Independently: `EmitThrow` preserves the heap operand for Constant / Variable / New / ThrowExpression; ScopeValidator declares catch `VariableName` and the VM catch body reads Message; Resume PC dispatch jumps to a label after suspend+exit so later statements run (`x==2`); unmatched `Member` invoke Errors at analyze and `Interpreter.Compile` rejects; `EscapedVariables` is asserted for invoke-arg / return / foreach collection. LINQ throw/catch siblings already preserved operand / bound the name; VM now matches those contracts on the tested paths. Spot-check of F4–F11 / F13–F20 / F22 / F23 oracles: still present, not regressed.

## Counts (primary evidence this session)

| Item | Count | Evidence |
|------|------:|----------|
| HEAD SHA | `ad6b427f4d739204bb184dbf630ed83cf1802fda` | `git rev-parse HEAD`; matches `gh pr view 53` `headRefOid` |
| merge-base | `a6735bebd89a948946ef0dc5c4cb7ed80b981f55` | `git merge-base origin/master HEAD` |
| `Poly/**/*.cs` in PR (`merge-base..HEAD`) | 6 | `SyntaxTypeCompatibilityAnalyzer.cs`, `TypeAndMemberResolutionPass.cs`, `VariableLifetimePass.cs`, `DirectVmAbiEmitter.AbiCtx.cs`, `DirectVmAbiEmitter.Statements.cs`, `DirectVmAbiEmitter.cs` |
| `Poly.Tests/Interpretation/*.cs` files | 62 | `find … -name '*.cs'` |
| Interpretation `[Test` (incl. `[Test, Timeout`) | **930** | per-file `rg -c '\[Test'` sum (Python) |
| Interpretation exact `^\s*\[Test\]` | 838 | misses Timeout; do not use as the count |
| Interpretation `[Test, Timeout` | 92 | 838+92=930 |
| merge-base Interpretation `[Test` | **827** | `git grep -c '\[Test' a6735beb -- Poly.Tests/Interpretation` sum; **+103** |
| `33fefd2b` Interpretation `[Test` | 928 | +2 vs that SHA (`EscapedVariables_*` ×3 minus 1 removed resume test) |
| All `Poly.Tests` `[Test` attributes | **2705** | `rg -c '\[Test' Poly.Tests` sum |
| TUnit discovery / run | **2726 / 0** | `dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false` (parameterized extras vs attribute count) |

## Checklist

- [x] Diff collected (`33fefd2b..HEAD` product + oracle flips; `merge-base..HEAD` full PR)
- [x] Stance: adversarial re-verify; Nested / prior Sentinel quotes not trusted
- [x] Sibling-path: VM vs LINQ throw; VM vs LINQ catch bind; Member analyze vs emit InvokeNamed; Resume root vs `CompileFunctionBody`
- [x] Reachability before severity on F1/F2/F3/F12
- [x] F1 F2 F3 F12 F21 dispositioned from **current** source + tests
- [x] F4–F11 F13–F20 F22 F23 spot-checked (not full re-hunt)
- [x] Counts recomputed; 2726 is suite total, Interpretation `[Test` is 930
- [x] Review + follow-ups written (do not overwrite 33fefd2b or interpretation-coverage-sentinel files)

---

## F1 F2 F3 F12 F21 (this SHA)

| F# | Disposition | Evidence (this session) |
|----|-------------|-------------------------|
| **F1** | **fixed** | `DirectVmAbiEmitter.Statements.cs:324-338` compiles any throw operand, `HeapUnsafeGet`s the ring handle, `Throw`s that `Exception`. No `New`-only arm; no `Throw(New(typeof(Exception)))` discard. `ThrowExpression` shares the arm (`DirectVmAbiEmitter.cs:218`, `:478`). `EmitConstant` heap-allocates the Constant object (`DirectVmAbiEmitter.cs:270-272`); `Heap.Allocate` stores that reference (`Heap.cs:26-42`). Tests: `ThrowVmTests.cs:42-48`, `:52-58`, `:62-72` `IsSameReferenceAs(expected)` for Constant / ThrowExpression / Variable. LINQ sibling already `Expression.Throw(CompileNode(exception))` (`LinqExpressionGenerator.cs:268`). Reachable on legal `ThrowStatement(Constant(ex))` / `Variable`. |
| **F2** | **fixed** | `VariableLifetimePass.cs:80-82`, `:115-134`: `TryCatchFinally` arm registers a catch `Variable(name)` in `VariablesByName` before the body, so `ValidateVariableReference` no longer Errors `"not declared"`. Emitter writes the CLR catch param onto a synthetic slot (`Statements.cs:350-368`) and body reads via name fallback (`DirectVmAbiEmitter.AbiCtx.cs:282-293`, `:298-308`). Type seed so `Member(ex, "Message")` resolves (`TypeAndMemberResolutionPass.cs:14-17`, `:30-65`). Test: `ExceptionHandlingVmTests.cs:124-141` Compile+Execute, message `"boom-msg"`. LINQ sibling still binds via `DeclareParameter` (`LinqExpressionGenerator.ControlFlow.cs:427-440`). Original Compile-reject path is gone. Residual: name fallback is not the same `Variable` instance (see F24). |
| **F3** | **fixed** | `EmitSuspendNode` (`Statements.cs:384-412`): resume `Label` is **after** `Goto(ExitLabel)`; `saveResumeId` assigns `ctx.StatePcFlush` (`VmState.ProgramCounter`), not local `_pc`. Root compile **before** `EmitPcDispatch` so labels exist (`DirectVmAbiEmitter.cs:135-139`). Dispatch only when `Status==Resuming`, switch on state PC (`AbiCtx.cs:95-108`). `Interpreter.Resume` sets `Resuming` then re-invokes (`Interpreter.cs:142-147`); `ExecutionResult.Resume` (`ExecutionResult.cs:52-62`). Test: `SuspendResumeVmTests.cs:17-31` `resumed.IsSuspended==false` and `x==2`. First-run reachability: `Execute` sets `Running` (`Interpreter.cs:120`), dispatch skipped, suspend then resume fall-through. |
| **F12** | **fixed** | `CheckInvokeTarget` (`SyntaxTypeCompatibilityAnalyzer.cs:199-210`): `Member` with `GetResolvedMember` null **and** `!HasPlausibleMemberOverload` → `Report` → `ReportError` (`:811-812`). `Interpreter.Compile` fail-closed on Error (`Interpreter.cs:77-81`). Test: `MethodInvocationSemanticResolutionTests.cs:65-74` Error `"no matching member"` + Compile throws. `Substring(1.5)`: `HasPlausibleMemberOverload` (`:229-259`) — `string.Substring(int)` / `(int,int)`; `IsNumericWidening(double,int)` ranks 6 ≰ 3 (`:262-283`) → false → reject. Widening escape preserves `DateTime.AddDays(double)` with `long` (rank 4≤6). Emit `InvokeNamed` (`DirectVmAbiEmitter.Invoke.cs:108-110`) is not reached when Compile rejects. IndexOf(char) VM oracle still `:50-53`. |
| **F21** | **fixed** | Producer still marks escaped decls (`VariableLifetimePass.cs:98-107`, `:137-139`, `:232-236`). Tests: `VariableScopeTests.cs:57-68` invoke arg (`Invoke(fn, arg)` Delegate is Variable, so args marked), `:72-81` return, `:85-95` foreach collection. `rg EscapedVariables Poly.Tests/Interpretation` now hits those three tests (was empty at `33fefd2b`). |

---

## Spot-check previously-fixed F4–F11 F13–F20 F22 F23

Brief disposition only. Oracles still in tree; product `.cs` for these F# was not the `ad6b427f` hunk set (except shared emitter/analyzer files). Full suite 2726/0.

| F# | Disposition | One-line evidence this session |
|----|-------------|-------------------------------|
| F4 | **fixed** (not regressed) | `LanguageSurfaceTests.cs:137` `ClrTypeReference` in CompileReject samples; `:182-185` dedicated compile-reject |
| F5 | **fixed** (not regressed) | `LanguageSurfaceTests.cs:165` constructs `ResolvedTypeReference` |
| F6 | **fixed** (not regressed) | `InvalidProgramTests.cs:71-91` JT0005 + CF0001 sibling + JT0003; `JumpTargetAnalysisTests.cs:55-60` JT0003 |
| F7 | **fixed** (not regressed) | `If_ConstFalse_ElidesThenBranch` (`ControlFlowAnalysisTests.cs:350`); MustExecuteMetadata `:402-423`; CF0001/4/6/10/13 `:427-476` |
| F8 | **fixed** (not regressed) | `SideEffectAnalysisTests.cs:48-55` `EmitElisionDiagnostics` → `DEAD_CODE_ELIDABLE`; metadata `:17-88` |
| F9 | **fixed** (not regressed) | `DefiniteAssignmentTests.cs:75-83` metadata; if-merge `:45-72`; loop non-leak `:86-96` |
| F10 | **fixed** (not regressed) | `LambdaReturnTypeAnalyzerTests.cs:18-45` Invoke(Lambda) + stored Invoke(Variable) |
| F11 | **fixed** (not regressed) | `ConstantFoldingTests.cs:517-537` `GetNodeReplacement` Constant(5) + Compile+Execute 5; emitter honors replacement (`DirectVmAbiEmitter.cs:161-163`) |
| F13 | **fixed** (not regressed) | `TypeDefinitionNodeAnalyzerTests.cs:305-329` Optional/Map property runtime types |
| F14 | **fixed** (not regressed) | `AstConstructorDefinitionTests.cs:78-89` New(AST) fail-loud `"no matching constructor"`; `AstMemberVmTests.cs:23-51` Compile+Execute |
| F15 | **fixed** (not regressed) | `InvokeMemberInstanceTests.cs:140-159` AST method / no CLR host `"does not define method"` |
| F16 | **fixed** (not regressed) | `CSharpGeneratorTests.cs:841-879`; Union still Generate fallback (`CSharpGenerator.cs:1246-1248` `ToString()`, no `UnionTypeReference` arm) |
| F17 | **fixed** (not regressed) | `UsingStatementVmTests.cs:8-14` non-IDisposable skip; `:17-25` nested; `ForEachEnumeratorDisposeTests.cs:9-46`. Product: `IfThen(TypeIs IDisposable)` (`Statements.cs:636-638`, `:668-670`) |
| F18 | **fixed** (not regressed) | `ExceptionHandlingVmTests.cs:147-171` inner catch; `:175-199` throw-in-catch → outer |
| F19 | **fixed** (not regressed) | `InterpretResultAbiTests.cs:53-85` Break/Continue/Throw ≠ ResultKind.Break/Continue/Throw |
| F20 | **fixed** (not regressed) | `CallSiteCatalogTests.cs:35-43` + `:56-68`; TypeCast `:143-148` `typeof(double)`; Block `:118-123` last-expr `int`; `Member_OnNull_CompileRejects` (`InterpreterLanguageGotchaTests.cs:478-482`); TH0002 still dropped (`ThisReferenceTests.cs:150-161`); `PropertyDefinitionNodeTests.cs:22-34` Analyze |
| F22 | **fixed** (not regressed) | `VmHeapComparisonTests.cs:8-38` DateTime/DateOnly/Guid; mixed DateOnly/string compile-rejects |
| F23 | **fixed** (not regressed) | `MermaidAstGeneratorTests.cs:12-49` executable Add/If + AnalysisOnly `TypeDefinitionNode` |

---

## Sibling-path + reachability

| Semantic | Paths checked | Result |
|----------|---------------|--------|
| Throw operand identity | VM `EmitThrow` (all operands); LINQ `ThrowStatement` (`LinqExpressionGenerator.cs:268`); `ThrowExpression` → same VM arm | VM now matches LINQ ThrowStatement. LINQ has **no** `ThrowExpression` arm (unsupported) — pre-existing, not F1. |
| Catch `VariableName` bind | VM ScopeValidator + emitter synthetic + name fallback; LINQ `DeclareParameter` | Tested path (unique name `"ex"`) binds. LINQ still name-binds. Residual F24 on VM register name fallback vs outer same name. |
| Resume fall-through | Root `Emit` + `EmitPcDispatch` + `StatePcFlush`; `CompileFunctionBody` (`DirectVmAbiEmitter.Invoke.cs:468-531`) has **no** PC dispatch | F3 claim was root Block; that path is fixed. Lambda-body `SuspendNode` resume was not in F3’s oracle and is pre-existing (no dispatch on `fnCtx`). |
| Unmatched Member invoke | Analyze `CheckInvokeTarget` Error → Compile reject; emit `InvokeNamed` only if Compile succeeds | `Substring(1.5)` never reaches emit. `HasPlausibleMemberOverload` keeps DateTime.AddDays(long) late-bind. |
| EscapedVariables | `MarkSubtreeEscaped` on non-Lambda invoke args, Return, foreach collection; tests force those three | Immediate `Invoke(Lambda, arg)` is **not** marked (`VariableLifetimePass.cs:99-100`) — emit uses `CapturedBindings` for cells (`NeedsCell`), not this set. F21 asked the metadata oracle; it exists. |

---

## Issues

### Issue 1 -- Severity: suggestion (F24 residual of F2 name fallback)
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.AbiCtx.cs:298-308` (then `:445-457` `VariableReadRaw`)
- Description: Catch bind is **not** the same `Variable` instance the body uses (`new Variable(clause.VariableName)` at emit vs body `Variable("ex")`; dictionaries use `ReferenceEqualityComparer`). `TryGetVariable` name-walks `_scopeStack` inner-first (`Stack` enumerator) and is sound under shadow. `VariableReadRaw` consults `TryGetRegister` **first**. `TryGetRegister` name-walks the entire `_variableRegisters` dictionary (insertion order: outer declared first). On a legal program that already has a register-backed outer `Variable("ex")` plus `CatchClause.VariableName == "ex"` (ScopeValidator only **warns** shadow, `VariableLifetimePass.cs:188-190`; Compile does not fail on warnings), the catch body read can hit the **outer** register and skip the slot `VariableWrite(synthetic, handle)` just filled (`Statements.cs:364-367`). F2’s tested tree uses outer `"msg"` / catch `"ex"` — that path is correct. This is incomplete same-instance bind, not the original `"not declared"` bug.
- Suggestion: Resolve catch body `Variable` through `VariableReferences` (analysis decl) or a scope-ordered register lookup; or declare/read one shared `Variable` instance. Add a shadowing Compile+Execute oracle (catch `"ex"` vs outer `"ex"`) that asserts the catch Message, not the outer value.
- Status: open

No other open bugs on F1 F2 F3 F12 F21.
