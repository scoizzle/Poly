# Sentinel re-verify follow-ups — PR 53 `ad6b427f` — 2026-09-06

Source review: [`2026-09-06-pr53-ad6b427f-sentinel-rereview.md`](2026-09-06-pr53-ad6b427f-sentinel-rereview.md).

Prior inventory (do not overwrite): [`2026-09-06-interpretation-coverage-sentinel.md`](2026-09-06-interpretation-coverage-sentinel.md), [`2026-09-06-interpretation-coverage-sentinel-followups.md`](2026-09-06-interpretation-coverage-sentinel-followups.md), [`2026-09-06-pr53-33fefd2b-sentinel-rereview.md`](2026-09-06-pr53-33fefd2b-sentinel-rereview.md), [`2026-09-06-pr53-33fefd2b-sentinel-followups.md`](2026-09-06-pr53-33fefd2b-sentinel-followups.md).

**Owner:** optional residual F24 (catch name-fallback vs outer same-name register). F1 F2 F3 F12 F21 are closed on this SHA — do not re-open from 33fefd2b characterization oracles.

---

## Closed this product pass (`ad6b427f` vs `33fefd2b`)

- [x] **F1** — `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:324-338` — Preserve non-`New` throw operands as **that** heap exception instance. Oracles flipped: `ThrowVmTests.cs:42-72` `IsSameReferenceAs`. LINQ ThrowStatement already threw the compiled operand (`LinqExpressionGenerator.cs:268`).

- [x] **F2** — `Poly/Interpretation/Analysis/Semantics/VariableLifetimePass.cs:80-82,115-134` + `TypeAndMemberResolutionPass.cs:30-65` + `DirectVmAbiEmitter.Statements.cs:350-368` — Declare `CatchClause.VariableName` in ScopeValidator; catch body reads Message. Oracle: `ExceptionHandlingVmTests.cs:124-141` `"boom-msg"` (no longer Compile-reject `"not declared"`). LINQ sibling still binds (`LinqExpressionGenerator.ControlFlow.cs:437-440`).

- [x] **F3** — `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:384-412` + `DirectVmAbiEmitter.cs:135-139` — Resume label after suspend+exit; flush `VmState.ProgramCounter`; register labels before PC dispatch. Oracle: `SuspendResumeVmTests.cs:17-31` `x==2`. `Resume()` when not suspended remains `:8-14`.

- [x] **F12** — `Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs:199-210` — Unmatched Member invoke Errors so `Interpreter.Compile` rejects. Oracle: `MethodInvocationSemanticResolutionTests.cs:65-74`. IndexOf(char) VM oracle `:50-53`. Widening late-bind for `DateTime.AddDays(long)` preserved via `HasPlausibleMemberOverload`.

- [x] **F21** — `Poly.Tests/Interpretation/VariableScopeTests.cs:57-95` — Assert `VariableAnalysisMetadata.EscapedVariables` for invoke arg / return / foreach collection. Metadata, shadow warning, and `CapturedVariables` remain `:17-54`.

---

## Disposition of F1–F23 (current SHA `ad6b427f`)

| F# | Disposition | One-line evidence |
|----|-------------|-------------------|
| F1 | **fixed** | `EmitThrow` preserves heap operand instance; tests same-instance |
| F2 | **fixed** | ScopeValidator declares catch var; body reads Message |
| F3 | **fixed** | PC dispatch after root compile; resume label after exit; x==2 |
| F4 | **fixed** | `ClrTypeReference` in CompileReject samples + dedicated test |
| F5 | **fixed** | `ResolvedTypeReference` constructed in AnalysisOnly samples |
| F6 | **fixed** | JT0003 + JT0005 + CF0001 sibling pinned |
| F7 | **fixed** | CF0001/4/6/10/13; then-elision rename; MustExecuteMetadata |
| F8 | **fixed** | `SideEffectAnalysisTests` + `DEAD_CODE_ELIDABLE` flag on |
| F9 | **fixed** | `DefiniteAssignmentTests` metadata + merge + loop non-leak |
| F10 | **fixed** | `LambdaReturnTypeAnalyzerTests` inline + stored |
| F11 | **fixed** | Compile+Execute + `GetNodeReplacement` Constant(5) |
| F12 | **fixed** | unmatched Member Error + Compile-reject (widen late-bind preserved) |
| F13 | **fixed** | Optional/Map as property types |
| F14 | **fixed** | New(AST) fail-loud; `AstMemberVmTests` Compile+Execute |
| F15 | **fixed** | AST method / no CLR host fail-loud |
| F16 | **fixed** | C# printer cases + Union fallback string |
| F17 | **fixed** | non-IDisposable using skip; foreach enumerator Dispose |
| F18 | **fixed** | nested try execute + throw-in-catch |
| F19 | **fixed** | Break/Continue/Throw ≠ ResultKind.Break/Continue/Throw |
| F20 | **fixed** | theater renames/oracles in listed files |
| F21 | **fixed** | EscapedVariables assert invoke arg / return / foreach collection |
| F22 | **fixed** | DateTime/DateOnly/Guid heap compare + mixed compile-reject |
| F23 | **fixed** | `MermaidAstGeneratorTests` under Interpretation/ |

Product F1/F2/F3/F12/F21 closed this pass (product + flipped oracles). Suite **2726/0** this session. Interpretation `[Test` **930**.

---

## Open after this re-verify

- [ ] **F24** — `Poly/Interpretation/Vm/DirectVmAbiEmitter.AbiCtx.cs:298-308` — Catch body `Variable` is a different instance than the emit synthetic; `VariableReadRaw` uses `TryGetRegister` name walk over all live registers (outer insertion first) before scope-ordered `TryGetVariable`. On outer `ex` + catch `ex` (shadow **warning** only), catch body can read the outer register. Bind catch reads through analysis `VariableReferences` / inner-first registers, and add Compile+Execute Message oracle for that shape. Does **not** reopen F2.

---

## Process

`--filter FullyQualifiedName~Interpretation` is not a TUnit/MTP option on this host (`Unknown option '--filter'`). `--treenode-filter` with Interpretation/class globs listed all tests or ran **0**. Recompute Interpretation `[Test` with per-file `rg -c '\[Test'` sum; do not report 2726 as that count. Full `dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false` is the working suite command.

Characterization tests that asserted the **bug** (F1 discard, F3 re-suspend, F12 Compile-accepts) were flipped on this SHA. Do not restore those oracles.

Catch bind by **name fallback** is not “same instance.” F2’s original fail-loud is closed; F24 tracks the shadow sibling.
