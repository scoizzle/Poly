# Sentinel re-verify follow-ups — PR 53 `33fefd2b` — 2026-09-06

Source review: [`2026-09-06-pr53-33fefd2b-sentinel-rereview.md`](2026-09-06-pr53-33fefd2b-sentinel-rereview.md).

Prior inventory (do not overwrite): [`2026-09-06-interpretation-coverage-sentinel.md`](2026-09-06-interpretation-coverage-sentinel.md), [`2026-09-06-interpretation-coverage-sentinel-followups.md`](2026-09-06-interpretation-coverage-sentinel-followups.md).

**Owner:** product fixes for open F# below. Tests already characterize F1/F3/F12 current behavior — flip oracles when product changes. Do not silently “fix while testing.”

---

## Still open

- [ ] **F1** — `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:324-340` — Preserve non-`New` throw operands (`Constant` / `Variable` / `ThrowExpression`) as **that** exception instance. Flip `ThrowVmTests.cs:45-82` from discard characterization to same-instance. LINQ already throws the compiled operand (`LinqExpressionGenerator.cs:268`) — VM must match.

- [ ] **F2** — `Poly/Interpretation/Analysis/Semantics/VariableLifetimePass.cs:70-108` + `DirectVmAbiEmitter.Statements.cs:353-370` — Declare `CatchClause.VariableName` in ScopeValidator and bind the **same** instance the catch body reads. Today Compile rejects `"not declared"` (`ExceptionHandlingVmTests.cs:140-143`). LINQ sibling already binds (`LinqExpressionGenerator.ControlFlow.cs:437-440`). After bind: assert message/type in the catch body.

- [ ] **F3** — `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:387-409` — Resume must fall through past `SuspendNode` (label is currently **before** inner+suspend+exit). Flip `SuspendResumeVmTests.cs:34-48` to assert later statements run (`x==2`). `Resume()` when not suspended is already tested (`:8-14`).

- [ ] **F12** — `Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs:199-201` — Unmatched Member invoke (`Substring(1.5)`) must Error at analyze so `Interpreter.Compile` rejects. Flip `MethodInvocationSemanticResolutionTests.cs:65-71`. IndexOf(char) VM oracle is done (`:50-53`). Optionally Compile+Execute `IndexOf(string)` (Nested `-1` note is unverified).

- [ ] **F21** — `Poly.Tests/Interpretation/VariableScopeTests.cs` — Assert `VariableAnalysisMetadata.EscapedVariables` (invoke arg / return / foreach collection). Metadata, shadow warning, and `CapturedVariables` are already tested (`:17-54`).

---

## Disposition of F1–F23 (current SHA `33fefd2b`)

| F# | Disposition | One-line evidence |
|----|-------------|-------------------|
| F1 | **still open** | `EmitThrow` still `Throw(New(typeof(Exception)))` for non-`New`; tests assert discard |
| F2 | **still open** | no CatchClause in ScopeValidator; test asserts `"not declared"` |
| F3 | **still open** | resume label before suspend; tests assert re-suspend |
| F4 | **fixed** | `ClrTypeReference` in CompileReject samples + dedicated test |
| F5 | **fixed** | `ResolvedTypeReference` constructed in AnalysisOnly samples |
| F6 | **fixed** | JT0003 + JT0005 + CF0001 sibling pinned |
| F7 | **fixed** | CF0001/4/6/10/13; then-elision rename; MustExecuteMetadata |
| F8 | **fixed** | `SideEffectAnalysisTests` + `DEAD_CODE_ELIDABLE` flag on |
| F9 | **fixed** | `DefiniteAssignmentTests` metadata + merge + loop non-leak |
| F10 | **fixed** | `LambdaReturnTypeAnalyzerTests` inline + stored |
| F11 | **fixed** | Compile+Execute + `GetNodeReplacement` Constant(5) |
| F12 | **still open** | IndexOf(char) VM yes; unmatched Compile-accepts |
| F13 | **fixed** | Optional/Map as property types |
| F14 | **fixed** | New(AST) fail-loud; `AstMemberVmTests` Compile+Execute |
| F15 | **fixed** | AST method / no CLR host fail-loud |
| F16 | **fixed** | C# printer cases + Union fallback string |
| F17 | **fixed** | non-IDisposable using skip; foreach enumerator Dispose |
| F18 | **fixed** | nested try execute + throw-in-catch |
| F19 | **fixed** | Break/Continue/Throw ≠ ResultKind.Break/Continue/Throw |
| F20 | **fixed** | theater renames/oracles in listed files |
| F21 | **still open** | captured/shadow/metadata yes; EscapedVariables no |
| F22 | **fixed** | DateTime/DateOnly/Guid heap compare + mixed compile-reject |
| F23 | **fixed** | `MermaidAstGeneratorTests` under Interpretation/ |

Nested claim “F1–F23 closed test-only (2724/0)” is **false** as a close of F1–F23: product F1/F2/F3/F12 remain, F21 escaped leftover, and 2724 is not Interpretation (928 `[Test` this tree; all Poly.Tests 2703).

---

## Process

Characterization tests that assert the **bug** (F1 discard, F3 re-suspend, F12 Compile-accepts) are valid test-only oracles **only if** the product hook stays on the follow-ups list. Do not mark those F# `[x]` until product + flipped oracles are green.

`CheckInvokeTarget` early-return on `Member` is how unmatched overloads skip analyze-time fail-closed — same class as `UseAllAnalyzers` omitting SyntaxTypeCompatibility. Prefer `Interpreter.Analyze` / `Compile` reject for “no matching member” claims.
