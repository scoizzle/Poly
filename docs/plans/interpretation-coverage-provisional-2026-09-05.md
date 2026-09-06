# Provisional Interpretation coverage inventory — 2026-09-05

**Branch / PR:** `cleanup/interpretation-coverage` · [PR 53](https://github.com/scoizzle/Poly/pull/53)  
**Mode:** Product close of Sentinel re-verify open F# (F1 F2 F3 F12 F21). **Authority:** `docs/agent/reviews/2026-09-06-pr53-33fefd2b-sentinel-followups.md`.  
**Scope:** `Poly/Interpretation/**` product + flip tests in `Poly.Tests/Interpretation/**`. No PR 51 product.

---

## F# disposition (product close pass)

| F# | Status | Notes |
|----|--------|-------|
| F1 | **Closed (product)** | `EmitThrow` preserves non-`New` operand instance (`Constant` / `Variable` / `ThrowExpression` value). Tests: `Throw_*_PropagatesSameInstance`. |
| F2 | **Closed (product)** | ScopeValidator declares `CatchClause.VariableName`; TypeAndMember seeds catch types; emitter binds same instance (name fallback). Catch body reads `Message`. |
| F3 | **Closed (product)** | Resume label after SuspendNode exit; Resume falls through (`x==2`). |
| F4 | **Closed** | `ClrTypeReference` in `CompileRejectKinds_FailLoud` + dedicated compile-reject. |
| F5 | **Closed** | `ResolvedTypeReference` in `AnalysisOnlyKinds_AreNotScriptEntry`. |
| F6 | **Closed** | JT0003 + JT0005 pinned; CF0001 sibling asserted. |
| F7 | **Closed** | CF0001/4/6/10/13; rename const-false→then; MustExecuteMetadata asserted. |
| F8 | **Closed** | `SideEffectAnalysisTests` (DEAD_CODE_ELIDABLE + metadata). |
| F9 | **Closed** | `DefiniteAssignmentTests` metadata + if/else merge + loop non-leak. |
| F10 | **Closed** | `LambdaReturnTypeAnalyzerTests`. |
| F11 | **Closed** | `ConstantFoldingTests` Compile+Execute + `GetNodeReplacement`. |
| F12 | **Closed (product)** | Unmatched Member invoke (`Substring(1.5)`) Errors at analyze; `Interpreter.Compile` rejects. |
| F13 | **Closed** | Optional/Map property types in `TypeDefinitionNodeAnalyzerTests`. |
| F14 | **Closed** | New AST type Compile fail-loud; `AstMemberVmTests`. |
| F15 | **Closed** | AST method body / no CLR host fail-loud. |
| F16 | **Closed** | Missing C# printer cases + Map/Optional/Union. |
| F17 | **Closed** | Non-IDisposable using skip; foreach enumerator Dispose. |
| F18 | **Closed** | Nested try execute + throw-in-catch. |
| F19 | **Closed** | Break/Continue/Throw ≠ ResultKind.Break/Continue/Throw. |
| F20 | **Closed** | Theater renames/oracles. |
| F21 | **Closed** | `VariableScopeTests` metadata + shadow + captured + **EscapedVariables** (invoke arg / return / foreach collection). |
| F22 | **Closed** | `VmHeapComparisonTests` + extended `VmHeapRelationalTests`. |
| F23 | **Closed** | `MermaidAstGeneratorTests` under Interpretation/. |

### Suite status

Suite: **2726** total · **0** failed · **2726** succeeded (`dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false`).

### Remaining product hooks

None from F1–F23. Prior characterization-only oracles for F1/F3/F12 flipped to desired behavior.

---

## Bugs found this pass

- F1 `DirectVmAbiEmitter.Statements.cs` EmitThrow discarded non-New operands → fixed (preserve heap instance).
- F2 ScopeValidator missing CatchClause.VariableName + emitter identity → fixed (declare + type seed + name fallback).
- F3 Resume label before SuspendNode → fixed (label after exit Goto).
- F12 `CheckInvokeTarget` early-return on Member skipped unmatched overload → fixed (Error + Compile reject).
