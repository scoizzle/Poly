# Final Boss follow-ups — PR 53 `d658a34b` — 2026-09-06

Source review: [`2026-09-06-pr53-d658a34b-final-boss.md`](2026-09-06-pr53-d658a34b-final-boss.md).

Prior inventory (do not overwrite): [`2026-09-06-pr53-f25604fe-final-boss.md`](2026-09-06-pr53-f25604fe-final-boss.md), [`2026-09-06-pr53-f25604fe-final-boss-followups.md`](2026-09-06-pr53-f25604fe-final-boss-followups.md). Do not overwrite ad6b427f Final Boss files.

**Owner:** none required before ship. **F25 is still closed** at `d658a34b` after merge-master. F24 remains optional residual (do not block ship on F24). F1 F2 F3 F12 F21 stay closed. No new bug this re-verify.

---

## Closed this pass (re-verified at `d658a34b`)

- [x] **F25** — `Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs:269-270` — `IsNumericWidening` identity returns `NumericWidenRank(fromCode) is not null` (not `true`). `TypeCode.Object` has no rank (`:276-285` `_ => null`). `git diff f25604fe d658a34b --` this file is empty. Oracle: `MethodInvocationSemanticResolutionTests.cs:79-91` `Analyze_MakeRelativeUri_StringBuilder_NoMatch_ResolvedMemberNull_AndCompileRejects` — `GetResolvedMember` null, Error `"no matching member"`, `Interpreter.Compile` throws. Sibling `Analyze_DateTimeAddDays_Long_NumericWidening_CompileAccepts` (`:94-103`) still Compiles+Executes `2026-01-02` via rank 4≤6. Do not restore the pre-F25 `return true` identity shortcut. Merge of `origin/master` (`4d97319e`) did not touch this gate.

- [x] **F1** — `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:324-338` — Preserve non-`New` throw operands as **that** heap exception instance. Oracles: `ThrowVmTests.cs:42-72` `IsSameReferenceAs`. Unchanged vs `f25604fe`.

- [x] **F2** — `Poly/Interpretation/Analysis/Semantics/VariableLifetimePass.cs:80-82,115-134` + `TypeAndMemberResolutionPass.cs:30-65` + `DirectVmAbiEmitter.Statements.cs:350-368` — Declare `CatchClause.VariableName` in ScopeValidator; catch body reads Message. Oracle: `ExceptionHandlingVmTests.cs:124-141` `"boom-msg"`. Unique-name path only.

- [x] **F3** — `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:384-412` + `DirectVmAbiEmitter.cs:135-139` — Resume label after suspend+exit; flush `VmState.ProgramCounter`; register labels before PC dispatch. Oracle: `SuspendResumeVmTests.cs:17-31` `x==2`.

- [x] **F12** — unmatched Member invoke Errors + Compile-reject for Substring(1.5) (`MethodInvocationSemanticResolutionTests.cs:65-74`) **and** MakeRelativeUri(StringBuilder) (`:79-91`). AddDays(long) preserved (`:94-103`). F12 as fail-closed unmatched Member stays closed because F25 stays closed.

- [x] **F21** — `Poly.Tests/Interpretation/VariableScopeTests.cs:57-95` — Assert `VariableAnalysisMetadata.EscapedVariables` for invoke arg / return / foreach collection.

F4 F5 F6 F7 F8 F9 F10 F11 F13 F14 F15 F16 F17 F18 F19 F20 F22 F23: spot-checked oracles still present; not reopened. Merge-master did not edit Interpretation product.

---

## Open after this re-verify

- [ ] **F24** — `Poly/Interpretation/Vm/DirectVmAbiEmitter.AbiCtx.cs:298-308` — Catch body `Variable` is a different instance than the emit synthetic; `VariableReadRaw` uses `TryGetRegister` name walk over all live registers (outer insertion first) before scope-ordered `TryGetVariable`. On outer `ex` + catch `ex` (shadow **warning** only, `VariableLifetimePass.cs:188-190`), catch body can read the outer register. Bind catch reads through analysis `VariableReferences` / inner-first registers, and add Compile+Execute Message oracle for that shape. Does **not** reopen F2. Does **not** block ship by itself. Unchanged vs `f25604fe`; merge-master did not touch `AbiCtx.cs`; this SHA added no shadowing test.

---

## Disposition of F1–F25 (current SHA `d658a34b`)

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
| F12 | **fixed** | Substring(1.5) and MakeRelativeUri(StringBuilder) Error + Compile-reject; AddDays(long) Compiles |
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
| F24 | **open** (suggestion) | catch name-fallback vs outer same-name register; untested shadow; unchanged after merge |
| F25 | **fixed** | `IsNumericWidening` gated on numeric TypeCode; MakeRelativeUri(StringBuilder) Compile-rejects; AddDays(long) still Compiles; gate identical to `f25604fe` |

---

## Process

`--filter-class` is not a TUnit/MTP option on this host. `--treenode-filter` with Interpretation/class/method globs still listed **2737** tests (filter ignored). Recompute Interpretation `[Test` with per-file `\[Test` sum; do not report 2737 as that count. Full `dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false` is the working suite command. This session: **2737/0**. Interpretation `[Test` **932**. All `Poly.Tests` `[Test` **2713**.

Characterization tests that asserted the **bug** (F1 discard, F3 re-suspend, F12 Compile-accepts Substring(double), F25 Object==Object Compile-accept) must not be restored.

F12’s unmatched-Member claim now has a **non-numeric** same-arity mismatch oracle (MakeRelativeUri/StringBuilder) in addition to the rank-fail numeric case (Substring(double)). Keep both.

Catch bind by **name fallback** is not “same instance.” F2’s original fail-loud is closed; F24 tracks the shadow sibling and does not block ship.

Merge-master of PR 52 into this branch did not rewrite Interpretation. Future merge-master re-verifies must `git diff <prior-ship>...HEAD -- Poly/Interpretation` themselves — do not assume a merge commit is Interpretation-clean without that diff.
