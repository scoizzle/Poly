# Final Boss follow-ups — PR 53 `ad6b427f` — 2026-09-06

Source review: [`2026-09-06-pr53-ad6b427f-final-boss.md`](2026-09-06-pr53-ad6b427f-final-boss.md).

Prior inventory (do not overwrite): [`2026-09-06-interpretation-coverage-sentinel.md`](2026-09-06-interpretation-coverage-sentinel.md), [`2026-09-06-interpretation-coverage-sentinel-followups.md`](2026-09-06-interpretation-coverage-sentinel-followups.md), [`2026-09-06-pr53-33fefd2b-sentinel-rereview.md`](2026-09-06-pr53-33fefd2b-sentinel-rereview.md), [`2026-09-06-pr53-33fefd2b-sentinel-followups.md`](2026-09-06-pr53-33fefd2b-sentinel-followups.md), [`2026-09-06-pr53-ad6b427f-sentinel-rereview.md`](2026-09-06-pr53-ad6b427f-sentinel-rereview.md), [`2026-09-06-pr53-ad6b427f-sentinel-followups.md`](2026-09-06-pr53-ad6b427f-sentinel-followups.md).

**Owner:** product close of **F25** before ship. F24 remains optional residual (Chieftan: do not block ship on F24). F1 F2 F3 F21 stay closed on this SHA. F12’s Substring(1.5) oracle stays closed; F12 as fail-closed unmatched Member is **not** closed until F25 is.

---

## Closed this pass (re-verified at `ad6b427f`; do not restore 33fefd2b characterization oracles)

- [x] **F1** — `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:324-338` — Preserve non-`New` throw operands as **that** heap exception instance. Oracles: `ThrowVmTests.cs:42-72` `IsSameReferenceAs`. LINQ ThrowStatement already threw the compiled operand (`LinqExpressionGenerator.cs:268`).

- [x] **F2** — `Poly/Interpretation/Analysis/Semantics/VariableLifetimePass.cs:80-82,115-134` + `TypeAndMemberResolutionPass.cs:30-65` + `DirectVmAbiEmitter.Statements.cs:350-368` — Declare `CatchClause.VariableName` in ScopeValidator; catch body reads Message. Oracle: `ExceptionHandlingVmTests.cs:124-141` `"boom-msg"` (no longer Compile-reject `"not declared"`). Unique-name path only.

- [x] **F3** — `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:384-412` + `DirectVmAbiEmitter.cs:135-139` — Resume label after suspend+exit; flush `VmState.ProgramCounter`; register labels before PC dispatch. Oracle: `SuspendResumeVmTests.cs:17-31` `x==2`. `Resume()` when not suspended remains `:8-14`.

- [x] **F21** — `Poly.Tests/Interpretation/VariableScopeTests.cs:57-95` — Assert `VariableAnalysisMetadata.EscapedVariables` for invoke arg / return / foreach collection.

F4 F5 F6 F7 F8 F9 F10 F11 F13 F14 F15 F16 F17 F18 F19 F20 F22 F23: spot-checked oracles still present; not reopened.

---

## Open after this re-verify

- [ ] **F25** — `Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs:267-268` — **Ship block.** `IsNumericWidening` returns true on `Type.GetTypeCode` identity, including `TypeCode.Object` for unrelated classes. `HasPlausibleMemberOverload` then treats a same-name same-arity class-typed candidate as accepting any other Object-coded argument, so `CheckInvokeTarget` does not Error and `Interpreter.Compile` succeeds. Concrete: `Uri.MakeRelativeUri(StringBuilder)` (or equivalent). Drop the TypeCode-identity shortcut (or gate it on `NumericWidenRank != null`). Add Analyze Error + Compile-reject oracle for that shape. Keep `DateTime.AddDays(long)` as the numeric-widening sibling (rank 4≤6 already). Do **not** treat green 2726 as coverage of this hole.

- [ ] **F24** — `Poly/Interpretation/Vm/DirectVmAbiEmitter.AbiCtx.cs:298-308` — Catch body `Variable` is a different instance than the emit synthetic; `VariableReadRaw` uses `TryGetRegister` name walk over all live registers (outer insertion first) before scope-ordered `TryGetVariable`. On outer `ex` + catch `ex` (shadow **warning** only), catch body can read the outer register. Bind catch reads through analysis `VariableReferences` / inner-first registers, and add Compile+Execute Message oracle for that shape. Does **not** reopen F2. Does **not** block ship by itself.

---

## Disposition of F1–F25 (current SHA `ad6b427f`)

| F# | Disposition | One-line evidence |
|----|-------------|-------------------|
| F1 | **fixed** | `EmitThrow` preserves heap operand instance; tests same-instance |
| F2 | **fixed** | ScopeValidator declares catch var; unique-name body reads Message |
| F3 | **fixed** | PC dispatch after root compile; resume label after exit; x==2 |
| F4 | **fixed** | `ClrTypeReference` in CompileReject samples + dedicated test |
| F5 | **fixed** | `ResolvedTypeReference` constructed in AnalysisOnly samples |
| F6 | **fixed** | JT0003 + JT0005 + CF0001 sibling pinned |
| F7 | **fixed** | CF0001/4/6/10/13; then-elision; MustExecuteMetadata |
| F8 | **fixed** | `SideEffectAnalysisTests` + `DEAD_CODE_ELIDABLE` flag on |
| F9 | **fixed** | `DefiniteAssignmentTests` metadata + merge + loop non-leak |
| F10 | **fixed** | `LambdaReturnTypeAnalyzerTests` inline + stored |
| F11 | **fixed** | Compile+Execute + `GetNodeReplacement` Constant(5) |
| F12 | **partial** | Substring(1.5) Error + Compile-reject holds; Object-coded same-arity hole is F25 |
| F13 | **fixed** | Optional/Map as property types |
| F14 | **fixed** | New(AST) fail-loud; `AstMemberVmTests` Compile+Execute |
| F15 | **fixed** | AST method / no CLR host fail-loud |
| F16 | **fixed** | C# printer cases in PR test diff |
| F17 | **fixed** | non-IDisposable using skip; nested using Dispose |
| F18 | **fixed** | nested try execute + throw-in-catch |
| F19 | **fixed** | Break/Continue/Throw ≠ ResultKind.Break/Continue/Throw |
| F20 | **fixed** | theater renames/oracles in listed files |
| F21 | **fixed** | EscapedVariables assert invoke arg / return / foreach collection |
| F22 | **fixed** | DateTime/DateOnly/Guid heap compare + mixed compile-reject |
| F23 | **fixed** | `MermaidAstGeneratorTests` under Interpretation/ |
| F24 | **open** (suggestion) | catch name-fallback vs outer same-name register; untested shadow |
| F25 | **open** (bug, ship block) | `IsNumericWidening` TypeCode identity; unmatched class overload still Compiles |

---

## Process

`--filter FullyQualifiedName~Interpretation` is not a TUnit/MTP option on this host. `--treenode-filter` with Interpretation/class/method globs still listed **2726** tests (filter ignored). Recompute Interpretation `[Test` with per-file `\[Test` sum; do not report 2726 as that count. Full `dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false` is the working suite command. This session: **2726/0**. Interpretation `[Test` **930**.

Characterization tests that asserted the **bug** (F1 discard, F3 re-suspend, F12 Compile-accepts Substring(double)) were flipped on this SHA. Do not restore those oracles.

F12’s Substring(double) flip is not a complete fail-closed for unmatched Member. A second sibling (TypeCode.Object same-arity) still Compile-accepts. Gate: any “unmatched Member Errors” claim must force at least one **non-numeric** same-arity mismatch (no `object` overload), not only a rank-fail numeric case.

Catch bind by **name fallback** is not “same instance.” F2’s original fail-loud is closed; F24 tracks the shadow sibling.
