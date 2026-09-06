# PR 53 — interpretation-coverage (Final Boss) — 2026-09-06

- **Target**: PR 53 (`https://github.com/scoizzle/Poly/pull/53`, branch `cleanup/interpretation-coverage`) / PINNED worktree `/workspace/Poly-pr53-ad6b427f` / SHA `ad6b427f4d739204bb184dbf630ed83cf1802fda` vs `origin/master`
- **Mode**: re-verify (not rubber-stamp). Prior Sentinel at `docs/agent/reviews/2026-09-06-pr53-ad6b427f-sentinel-rereview.md` claimed ship, 0 bugs, F1 F2 F3 F12 F21 closed, residual F24 suggestion only. Each claim re-checked against `git show ad6b427f:PATH`, `git -C /workspace/Poly-pr53-ad6b427f diff origin/master...HEAD`, current source, and tests this session. No chain-trust of Sentinel quotes or 33fefd2b dispositions.
- **Model**: grok-4.6
- **SHA**: `ad6b427f4d739204bb184dbf630ed83cf1802fda` (`git rev-parse HEAD` matched; product HEAD not rewritten)
- **PINNED**: `/workspace/Poly-pr53-ad6b427f`
- **Issue counts**: 1 bug, 1 suggestion, 0 nits
- **Verdict**: not ship
- **Process notes**: Chieftan: do not block ship on F24 unless a **new** bug. New bug is F25 (HasPlausible `IsNumericWidening` treats `TypeCode` identity as numeric widen, including `TypeCode.Object`). F24 stays suggestion and is not the ship block. `--filter FullyQualifiedName~Interpretation` is not an MTP option. `--treenode-filter` with class/method globs still discovered **2726** tests (filter ignored). Full suite this session: **2726 / 0**. Interpretation `[Test` attribute count recomputed **930** (838 exact `^\s*\[Test\]` + 92 `[Test, Timeout`). Do not overwrite Sentinel files or prior SHA reviews (`33fefd2b`, `interpretation-coverage-sentinel`).

## Summary

`ad6b427f` is the product pass after Sentinel not-ship on `33fefd2b`. Independently: `EmitThrow` preserves the heap operand for Constant / Variable / New / ThrowExpression (F1 tests `IsSameReferenceAs`); ScopeValidator declares catch `VariableName` and the unique-name VM catch body reads `"boom-msg"` (F2); Resume PC dispatch jumps to a label after suspend+exit so later statements run (`x==2`) (F3); `EscapedVariables` is asserted for invoke-arg / return / foreach collection (F21). F12’s **tested** oracle (`Substring(1.5)` Error + Compile-reject) holds. F12 is **not** closed: `HasPlausibleMemberOverload` consults `IsNumericWidening`, and `fromCode == toCode return true` (`SyntaxTypeCompatibilityAnalyzer.cs:267-268`) treats any same `TypeCode` as a widen — including `TypeCode.Object` for unrelated classes — so unmatched same-arity reference overloads still Compile. That is a sibling hole in the new fail-closed predicate, reachable on a legal AST, untested. Residual F24 (catch name-fallback vs outer same-name register) is real and still untested; it does not reopen F2’s unique-name path and is not the ship block.

## Counts (primary evidence this session)

| Item | Count | Evidence |
|------|------:|----------|
| HEAD SHA | `ad6b427f4d739204bb184dbf630ed83cf1802fda` | `git rev-parse HEAD`; matches requested pin |
| merge-base vs `origin/master` | `a6735bebd89a948946ef0dc5c4cb7ed80b981f55` | `git merge-base origin/master HEAD` |
| `origin/master` | `4d97319e20cafb32e60e0143f648395237da1459` | `git rev-parse origin/master` |
| Product `Poly/**/*.cs` in PR (`origin/master...HEAD`) | **6** | `SyntaxTypeCompatibilityAnalyzer.cs`, `TypeAndMemberResolutionPass.cs`, `VariableLifetimePass.cs`, `DirectVmAbiEmitter.AbiCtx.cs`, `DirectVmAbiEmitter.Statements.cs`, `DirectVmAbiEmitter.cs` |
| `Poly.Tests/Interpretation/*.cs` files | **62** | walk of `Poly.Tests/Interpretation` |
| Interpretation `[Test` (incl. `[Test, Timeout`) | **930** | per-file `\[Test` sum |
| Interpretation exact `^\s*\[Test\]` | **838** | misses Timeout; do not use as the count |
| Interpretation `[Test, Timeout` | **92** | 838+92=930 |
| merge-base Interpretation `[Test` | **827** | `git grep -c '\[Test' a6735beb -- Poly.Tests/Interpretation` sum; **+103** |
| `33fefd2b` Interpretation `[Test` | **928** | +2 vs that SHA |
| All `Poly.Tests` `[Test` attributes | **2705** | `\[Test` sum under `Poly.Tests` |
| TUnit discovery / run | **2726 / 0** | `dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false` (parameterized extras vs attribute count) |

## Checklist

- [x] Diff collected (`origin/master...HEAD` full PR; `33fefd2b..HEAD` product + oracle flips)
- [x] Stance: adversarial re-verify; Sentinel / Nested / 33fefd2b quotes not trusted
- [x] Sibling-path: VM vs LINQ throw; VM vs LINQ catch bind; Member analyze vs emit InvokeNamed; Resume root vs `CompileFunctionBody`; F12 numeric vs `TypeCode.Object`
- [x] Reachability before severity on F1/F2/F3/F12/F25/F24
- [x] F1 F2 F3 F12 F21 dispositioned from **current** source + tests
- [x] F4–F11 F13–F20 F22 F23 spot-checked (not full re-hunt)
- [x] Counts recomputed; 2726 is suite total, Interpretation `[Test` is 930
- [x] Review + follow-ups written (do not overwrite 33fefd2b or interpretation-coverage-sentinel files)

---

## F1 F2 F3 F12 F21 (this SHA)

| F# | Disposition | Evidence (this session) |
|----|-------------|-------------------------|
| **F1** | **fixed** | `DirectVmAbiEmitter.Statements.cs:324-338` compiles any throw operand, `HeapUnsafeGet`s the ring handle, `Throw`s that `Exception`. No `New`-only arm; no `Throw(New(typeof(Exception)))` discard. `ThrowExpression` shares the arm (`DirectVmAbiEmitter.cs:218`). `EmitConstant` heap-allocates the Constant object (`DirectVmAbiEmitter.cs:270-272`); `Heap.Allocate` stores that reference (`Heap.cs:26-42`); `UnsafeGet` returns it (`Heap.cs:76`). Tests: `ThrowVmTests.cs:42-48`, `:52-58`, `:62-72` `IsSameReferenceAs(expected)` for Constant / ThrowExpression / Variable. LINQ sibling already `Expression.Throw(CompileNode(exception))` (`LinqExpressionGenerator.cs:268`). Reachable on legal `ThrowStatement(Constant(ex))` / `Variable`. |
| **F2** | **fixed** (tested unique-name path) | `VariableLifetimePass.cs:80-82`, `:115-134`: `TryCatchFinally` arm registers a catch `Variable(name)` in `VariablesByName` before the body, so `ValidateVariableReference` no longer Errors `"not declared"`. Emitter writes the CLR catch param onto a synthetic slot (`Statements.cs:350-368`) and body reads via name fallback (`DirectVmAbiEmitter.AbiCtx.cs:282-296`, `:298-309`, `:447-453`). Type seed so `Member(ex, "Message")` resolves (`TypeAndMemberResolutionPass.cs:14-17`, `:30-65`). Test: `ExceptionHandlingVmTests.cs:124-141` Compile+Execute, message `"boom-msg"`. LINQ sibling binds a **Parameter** node (`LinqExpressionGenerator.ControlFlow.cs:427-440`), not a `Variable` — VM is canonical; F2 oracle is Interpreter. Original Compile-reject path is gone. Residual name-fallback vs outer same-name register is F24, not a reopen of F2. |
| **F3** | **fixed** (root Block) | `EmitSuspendNode` (`Statements.cs:384-412`): resume `Label` is **after** `Goto(ExitLabel)`; `saveResumeId` assigns `ctx.StatePcFlush` (`VmState.ProgramCounter` at `AbiCtx.cs:110`), not local `_pc` (`AbiCtx.cs:32`). Root compile **before** `EmitPcDispatch` so labels exist (`DirectVmAbiEmitter.cs:135-139`). Dispatch only when `Status==Resuming`, switch on state PC (`AbiCtx.cs:95-108`). `Interpreter.Resume` sets `Resuming` then re-invokes (`Interpreter.cs:142-147`); `ExecutionResult.Resume` (`ExecutionResult.cs:52-62`). Test: `SuspendResumeVmTests.cs:17-31` `resumed.IsSuspended==false` and `x==2`. First-run: `Execute` sets `Running` (`Interpreter.cs:120`), dispatch skipped. `CompileFunctionBody` (`DirectVmAbiEmitter.Invoke.cs:468-531`) still has **no** PC dispatch — pre-existing sibling, not F3’s oracle. |
| **F12** | **partial** — tested numeric oracle holds; fail-closed sibling is F25 | `CheckInvokeTarget` (`SyntaxTypeCompatibilityAnalyzer.cs:199-210`): `Member` with `GetResolvedMember` null **and** `!HasPlausibleMemberOverload` → `Report` → `ReportError` (`:811-812`). `Interpreter.Compile` fail-closed on Error (`Interpreter.cs:77-81`). Test: `MethodInvocationSemanticResolutionTests.cs:65-74` Error `"no matching member"` + Compile throws. `Substring(1.5)`: `string.Substring(int)` / `(int,int)`; `IsNumericWidening(double,int)` ranks 6 ≰ 3 (`:262-283`) → false → reject. IndexOf(char) VM oracle still `:50-53`. **Hole:** `IsNumericWidening` `:267-268` `if (fromCode == toCode) return true` is not numeric widening. `Type.GetTypeCode` of unrelated classes is `TypeCode.Object`; a same-name same-arity candidate whose param is a class then accepts any other class as “plausible,” so Compile does **not** reject. Stated invariant at `:203-206` (“assign or widen”; “numeric widening”) is false for that branch. Emit `InvokeNamed` (`DirectVmAbiEmitter.Invoke.cs:108-110`) is then reached. DateTime.AddDays(long) still preserved via rank 4≤6 **without** the Object shortcut. |
| **F21** | **fixed** | Producer still marks escaped decls (`VariableLifetimePass.cs:98-107`, `:137-139`, `:232-236`). Tests: `VariableScopeTests.cs:57-68` invoke arg (`Invoke(fn, arg)` Delegate is Variable, so args marked), `:72-81` return, `:85-95` foreach collection. `rg EscapedVariables Poly.Tests/Interpretation` hits those three tests. Immediate `Invoke(Lambda, arg)` is **not** marked (`:99-100`) — emit uses `CapturedBindings` for cells (`NeedsCell`), not this set. F21 asked the metadata oracle; it exists. |

---

## Spot-check previously-fixed F4–F11 F13–F20 F22 F23

Brief disposition only. Oracles still in tree; product `.cs` for these F# was not the `ad6b427f` hunk set except shared emitter/analyzer files. Full suite 2726/0 this session.

| F# | Disposition | One-line evidence this session |
|----|-------------|-------------------------------|
| F4 | **fixed** (not regressed) | `LanguageSurfaceTests.cs:137` `ClrTypeReference` in CompileReject samples; `:182-185` dedicated compile-reject |
| F5 | **fixed** (not regressed) | `LanguageSurfaceTests.cs:165` constructs `ResolvedTypeReference` |
| F6 | **fixed** (not regressed) | `InvalidProgramTests.cs:71-91` JT0005 + CF0001 sibling + JT0003; `JumpTargetAnalysisTests.cs:55-60` JT0003 |
| F7 | **fixed** (not regressed) | `If_ConstFalse_ElidesThenBranch` (`ControlFlowAnalysisTests.cs:350`); MustExecuteMetadata / CF codes remain in that file |
| F8 | **fixed** (not regressed) | `SideEffectAnalysisTests.cs:48-55` `EmitElisionDiagnostics` → `DEAD_CODE_ELIDABLE` |
| F9 | **fixed** (not regressed) | `DefiniteAssignmentTests.cs:75-83` metadata; loop non-leak `:86-96` |
| F10 | **fixed** (not regressed) | `LambdaReturnTypeAnalyzerTests.cs:18-45` Invoke(Lambda) + stored Invoke(Variable) |
| F11 | **fixed** (not regressed) | `ConstantFoldingTests.cs:517-537` `GetNodeReplacement` Constant(5) + Compile+Execute 5; emitter honors replacement (`DirectVmAbiEmitter.cs:161-163`) |
| F13 | **fixed** (not regressed) | `TypeDefinitionNodeAnalyzerTests.cs` Optional/Map property runtime types still present in PR test diff |
| F14 | **fixed** (not regressed) | `AstConstructorDefinitionTests.cs:78-89` New(AST) fail-loud `"no matching constructor"` |
| F15 | **fixed** (not regressed) | `InvokeMemberInstanceTests.cs` AST method / no CLR host fail-loud remains in PR test diff |
| F16 | **fixed** (not regressed) | `CSharpGeneratorTests.cs` printer cases in PR test diff |
| F17 | **fixed** (not regressed) | `UsingStatementVmTests.cs:8-14` non-IDisposable skip; `:17-25` nested |
| F18 | **fixed** (not regressed) | `ExceptionHandlingVmTests.cs:147-171` inner catch; `:175-199` throw-in-catch → outer |
| F19 | **fixed** (not regressed) | `InterpretResultAbiTests.cs:53-85` Break/Continue/Throw ≠ ResultKind.Break/Continue/Throw |
| F20 | **fixed** (not regressed) | CallSiteCatalog / TypeCast / Block / Member_OnNull / TH0002 / PropertyDefinitionNode tests remain in PR test diff |
| F22 | **fixed** (not regressed) | `VmHeapComparisonTests.cs:8-38` DateTime/DateOnly/Guid; mixed DateOnly/string compile-rejects |
| F23 | **fixed** (not regressed) | `MermaidAstGeneratorTests.cs` under Interpretation/ (new in PR) |

---

## Sibling-path + reachability

| Semantic | Paths checked | Result |
|----------|---------------|--------|
| Throw operand identity | VM `EmitThrow` (all operands, `Statements.cs:324-338`); LINQ `ThrowStatement` (`LinqExpressionGenerator.cs:268`); `ThrowExpression` → same VM arm (`DirectVmAbiEmitter.cs:218`) | VM now matches LINQ ThrowStatement. LINQ has **no** `ThrowExpression` arm (unsupported) — pre-existing, not F1. |
| Catch `VariableName` bind | VM ScopeValidator + emitter synthetic + name fallback; LINQ `DeclareParameter` of a **Parameter** node (`LinqExpressionGenerator.ControlFlow.cs:437-440`) | Tested path (unique name `"ex"`) binds on VM. LINQ would not resolve a body `Variable("ex")` (reference-equality maps, no name fallback). VM is canonical; F2 oracle is Interpreter. Residual F24 on VM register name fallback vs outer same name. |
| Resume fall-through | Root `Emit` + `EmitPcDispatch` + `StatePcFlush`; `CompileFunctionBody` (`DirectVmAbiEmitter.Invoke.cs:468-531`) has **no** PC dispatch | F3 claim was root Block; that path is fixed. Lambda-body `SuspendNode` resume is pre-existing (no dispatch on `fnCtx`). |
| Unmatched Member invoke | Analyze `CheckInvokeTarget` Error → Compile reject; emit `InvokeNamed` only if Compile succeeds | `Substring(1.5)` never reaches emit (tested). `HasPlausibleMemberOverload` keeps DateTime.AddDays(long) late-bind via rank 4≤6. **Sibling still wrong:** `IsNumericWidening` TypeCode identity (`:267-268`) lets unmatched same-arity **reference** overloads Compile (F25). Unknown receiver / null arg type return `true` (`:232-238`) is additional fail-open; incomplete, not the F25 concrete case. |
| EscapedVariables | `MarkSubtreeEscaped` on non-Lambda invoke args, Return, foreach collection; tests force those three | Immediate `Invoke(Lambda, arg)` is **not** marked (`VariableLifetimePass.cs:99-100`). F21 asked the metadata oracle; it exists. |

### Reachability → severity (F25)

On a **valid AST** (well-typed construction, analysis complete):

```csharp
new Invoke(
    new Member(new Constant(new Uri("http://example.com/")), "MakeRelativeUri"),
    new Constant(new StringBuilder()))
```

`Uri.MakeRelativeUri(Uri)` is same name/arity; `StringBuilder` is not assignable to `Uri`; `GetResolvedMember` is null; both CLR types have `TypeCode.Object`; `IsNumericWidening` returns true; `HasPlausibleMemberOverload` returns true; `CheckInvokeTarget` does **not** Report; `Interpreter.Compile` succeeds. Then emit `InvokeNamed` (`DirectVmAbiEmitter.Invoke.cs:108-110`) runs at execution. This is fail-open on unmatched Member invoke — the F12 contract — on legal inputs. Severity **bug**. Not unreachable, not corrupt-metadata-only.

F24 reachability: legal program with outer `Variable("ex")` plus `CatchClause.VariableName == "ex"` (ScopeValidator **warns** shadow at `VariableLifetimePass.cs:188-190`; Compile does not fail on warnings). Catch body `Variable("ex")` can hit the outer register. Real, untested. Severity stays **suggestion** (Chieftan: do not block ship on F24). Does not reopen F2.

---

## Invariant-stating comments

- `Statements.cs:325-326`: preserve operand instance; match LINQ Throw. Holds on VM ThrowStatement / ThrowExpression / Constant / Variable / New. LINQ has no ThrowExpression arm (pre-existing).
- `Statements.cs:395-403`: flush step to `VmState.ProgramCounter`; resume label after suspend+exit. Holds on root compile. `CompileFunctionBody` never emits `EmitPcDispatch` — comment is local to `EmitSuspendNode` + root `Emit`, not a global “resume always works.”
- `DirectVmAbiEmitter.cs:135`: compile root before PC dispatch so labels exist. Holds.
- `AbiCtx.cs:286`: name fallback because catch `VariableName` is a string. True, and the fallback is what makes F2’s unique-name test pass. It is also what makes F24 possible (`TryGetRegister` is not inner-first).
- `SyntaxTypeCompatibilityAnalyzer.cs:202-206`: unmatched Member must Error; preserve late-bind only for numeric widening the scorer misses; reject when no same-name/arity candidate can accept via **assign or widen**. **Violated** by `IsNumericWidening` `:267-268` (Issue 1 / F25).

---

## Issues

### Issue 1 -- Severity: bug (F25)
- File: `Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs:267-268` (called from `:251-252` / `HasPlausibleMemberOverload` `:229-260`; gate `CheckInvokeTarget` `:205-210`)
- Description: F12 added `HasPlausibleMemberOverload` so unmatched Member invoke Errors and `Interpreter.Compile` rejects, while preserving `DateTime.AddDays(double)` with a `long` arg. `IsNumericWidening` returns true whenever `Type.GetTypeCode` of the two CLR types is equal (`:267-268`), **before** `NumericWidenRank`. `TypeCode.Object` is the code for classes, interfaces, arrays, `Guid`, `DateOnly`, `Uri`, `StringBuilder`, and most non-primitives. So any same-name same-arity candidate whose parameter is a reference type (or other Object-coded type) is treated as able to accept any other Object-coded argument. `CheckInvokeTarget` then skips `Report`. Concrete: `Invoke(Member(Constant(uri), "MakeRelativeUri"), Constant(new StringBuilder()))` — `GetResolvedMember` null, `HasPlausible` true, Compile succeeds, emit reaches `InvokeNamed`. The Substring(1.5) oracle does not force this sibling (`MethodInvocationSemanticResolutionTests.cs:65-74` is double→int, ranks 6 ≰ 3). The AddDays(long) escape is already true via rank 4≤6 (`:269-271`); the TypeCode-identity shortcut is unnecessary for that case and false advertising of the comment at `:203-206`. Suite 2726/0 does not cover it.
- Suggestion: `IsNumericWidening` must return true only when `NumericWidenRank` is non-null for both codes (drop `:267-268`, or gate it on numeric ranks). Keep `IsAssignableFrom` for reference conversions. Add Compile-reject oracle: `Uri.MakeRelativeUri(StringBuilder)` (or any same-arity class mismatch with no `object` overload) Error `"no matching member"` + `Interpreter.Compile` throws. Keep `DateTime.AddDays(long)` Compile+Execute as the widening sibling.
- Status: open
- Reachability: **valid AST, complete analysis** — unmatched same-arity Member invoke with unrelated class argument. Not corrupt metadata. Not dead.

### Issue 2 -- Severity: suggestion (F24 residual of F2 name fallback)
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.AbiCtx.cs:298-308` (then `:447-453` `VariableReadRaw`)
- Description: Catch bind is **not** the same `Variable` instance the body uses (`new Variable(clause.VariableName)` at emit `Statements.cs:356-357` vs body `Variable("ex")`; dictionaries use `ReferenceEqualityComparer` at `:178`). `TryGetVariable` name-walks `_scopeStack` inner-first (`Stack` enumerator, `:282-296`) and is sound under shadow. `VariableReadRaw` consults `TryGetRegister` **first**. `TryGetRegister` name-walks the entire `_variableRegisters` dictionary (insertion order: outer declared first, `:298-308`). `DeclareVariable` puts catch synthetics **and** outer locals into registers when a free register exists (`:234-259`, max 32). On a legal program that already has a register-backed outer `Variable("ex")` plus `CatchClause.VariableName == "ex"` (ScopeValidator only **warns** shadow, `VariableLifetimePass.cs:188-190`; Compile does not fail on warnings), the catch body read can hit the **outer** register and skip the slot `VariableWrite(synthetic, handle)` just filled (`Statements.cs:364-367`). F2’s tested tree uses outer `"msg"` / catch `"ex"` (`ExceptionHandlingVmTests.cs:124-141`) — that path is correct. This is incomplete same-instance bind, not the original `"not declared"` bug. Analysis already records the decl in `VariableReferences` (`VariableLifetimePass.cs:198-202`); emit does not consume it.
- Suggestion: Resolve catch body `Variable` through analysis `VariableReferences` or a scope-ordered register lookup; or declare/read one shared `Variable` instance. Add a shadowing Compile+Execute oracle (catch `"ex"` vs outer `"ex"`) that asserts the catch Message, not the outer value.
- Status: open

No other open bugs on F1 F2 F3 F21. F12’s numeric test path is green; F12 as a fail-closed contract is not, because of F25.

## Oracle / verification (read-only)

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false
total: 2726, failed: 0, succeeded: 2726, skipped: 0
```

`--treenode-filter` with `ThrowVmTests`, `/*/*ThrowVmTests/*`, and method-name globs still reported `found 2726 test(s)` on `--list-tests`. Full suite used. Green does **not** refute F25 (no Object-coded unmatched-arity-1 class mismatch oracle).
