# Provisional Interpretation coverage inventory — 2026-09-05

**Branch / PR:** `cleanup/interpretation-coverage` · [PR 53](https://github.com/scoizzle/Poly/pull/53)  
**Mode:** Sentinel F1–F23 Nested test-only close (worktree). **Authority:** `docs/agent/reviews/2026-09-06-interpretation-coverage-sentinel-followups.md`.  
**Scope:** `Poly.Tests/Interpretation/**` only. No `Poly/**` product edits.

---

## F# disposition (this pass)

| F# | Status | Notes |
|----|--------|-------|
| F1 | **Closed (current behavior)** | `ThrowVmTests` asserts product gap: non-`New` operands (`Constant` / `Variable` / `ThrowExpression`) discard and throw a fresh `Exception()` (`*_DiscardsOperand_ThrowsFreshException`). Desired same-instance propagate needs product fix. |
| F2 | **Closed** | Catch `VariableName` read fails loud (`not declared`) — asserted in `ExceptionHandlingVmTests`. |
| F3 | **Closed (current behavior)** | `Resume_WhenNotSuspended_Throws` + Resume API callable **closed**. `Suspend_ThenResume_ReSuspendsWithoutFallThrough` asserts product PC gap (Resume re-enters SuspendNode; no fall-through). Desired continue-remaining needs product fix. |
| F4 | **Closed** | `ClrTypeReference` in `CompileRejectKinds_FailLoud` + dedicated compile-reject. |
| F5 | **Closed** | `ResolvedTypeReference` in `AnalysisOnlyKinds_AreNotScriptEntry`. |
| F6 | **Closed** | JT0003 + JT0005 pinned; CF0001 sibling asserted (`InvalidProgramTests` + `JumpTargetAnalysisTests`). |
| F7 | **Closed** | CF0001/4/6/10/13; rename const-false→then; MustExecuteMetadata asserted. |
| F8 | **Closed** | `SideEffectAnalysisTests` (DEAD_CODE_ELIDABLE + SideEffect/Elision/AssignmentValueUsed metadata). |
| F9 | **Closed** | `DefiniteAssignmentTests` metadata + if/else merge + loop non-leak. |
| F10 | **Closed** | `LambdaReturnTypeAnalyzerTests`. |
| F11 | **Closed** | `ConstantFoldingTests` Compile+Execute + `GetNodeReplacement`. |
| F12 | **Closed (current behavior)** | IndexOf(char) Compile+Execute **closed**; Equals(string) as string-arg sibling (`IndexOf(string)` VM returns -1). Substring(1.5): Analyze resolved-member null + Compile currently accepts (characterization). Desired Compile-reject needs product. |
| F13 | **Closed** | Optional/Map property types in `TypeDefinitionNodeAnalyzerTests`. |
| F14 | **Closed** | New AST type Compile fail-loud; `AstMemberVmTests` Member/Assignment Compile+Execute. |
| F15 | **Closed** | AST method body / no CLR host fail-loud in `InvokeMemberInstanceTests`. |
| F16 | **Closed** | Missing C# printer cases + Map/Optional/Union (Union = Generate fallback string). |
| F17 | **Closed** | Non-IDisposable using skip; nested using; foreach enumerator Dispose after complete/break/throw. |
| F18 | **Closed** | Nested try execute + throw-in-catch in `ExceptionHandlingVmTests`. |
| F19 | **Closed** | Break/Continue/Throw are not `InterpreterResult` Break/Continue/Throw kinds. |
| F20 | **Closed** | Theater renames/fixes (CallSiteCatalog, CF then-elision, TypeCast/Block resolved type, Member_OnNull compile-reject, TH0002 dropped, Property Analyze). |
| F21 | **Closed** | `VariableScopeTests` metadata + shadow warning + captured set. |
| F22 | **Closed** | `VmHeapComparisonTests` + extended `VmHeapRelationalTests` (DateTime/Guid/mixed). |
| F23 | **Closed** | `MermaidAstGeneratorTests` executable + AnalysisOnly TypeDefinition smoke. |

### Suite status

Suite: **2724** total · **0** failed · **2724** succeeded (Nested close pass after F1/F3/F12 current-behavior asserts).

### Remaining product hooks (separate product PR — do not silently fix in `Poly/**`)

1. **F1** — `DirectVmAbiEmitter.Statements.cs` `EmitThrow`: preserve non-`New` operand instance (`Constant` / `Variable` / `ThrowExpression`). Tests: `Throw_*_DiscardsOperand_ThrowsFreshException`.
2. **F3** — Resume PC dispatch: fall through past `SuspendNode` instead of re-entering. Test: `Suspend_ThenResume_ReSuspendsWithoutFallThrough`.
3. **F12** — Unmatched overload Compile-reject (`Substring(1.5)`); optionally fix `IndexOf(string)` VM. Test: `Analyze_SubstringDouble_NoMatch_ResolvedMemberNull_AndCompileCurrentlyAccepts`.
4. **F2** (asserted fail-loud) — Catch variable binding when product should bind `VariableName`.

---

## What already existed (pre-Sentinel provisional)

See prior tables: AbiValueTypes, VmHeapRelational, JumpTarget, DefiniteAssignment, SideEffect, ValueStack, Mermaid smoke, ExceptionHandling augment, InterpretResultAbi augment.

---

## Bugs found this pass

None fixed in product (tests only). Confirmed product gaps (asserted as current behavior / documented): F1 throw non-New discard; F3 resume no fall-through; F12 unmatched overload not fail-closed; F2 catch binding absent (asserted fail-loud).
