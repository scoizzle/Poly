# TASK C1b - Dedicated constraint-failure exception; delete `AsVoidResultBody`

Status: planned. Branch slice/c1b-constraint-failure-v2. Lane B. Review 2. Implement mill: Grok (review on OpenCode).

Plan card: [`pipeline-convergence-plan.md` **C1b**](../../../domain-modeling/pipeline-convergence-plan.md) (line 383). Depends on C1a (merged #110 `b1fddf7e`). No open decision.

## Scope
Void export bodies (`OnEntry` / ctor / subscription) throw `InvalidOperationException` for fail-closed constraint and create-job failures (`EffectLoweringPass.cs:432-438` `AssignConstraintFailure` when `_context.ActionResultType` is null; `:1135-1146` void create fail-closed). Simulate cannot catch that at `Interpreter.Execute` (`Interpreter.cs:131-138`, no catch) because host fail-loud is the same type, so `BindForSimulate` (`DomainEntityInstance.cs:1019-1026`) rewrites `throw new InvalidOperationException(msg)` to `return DomainResult.Failure(msg)`, and `AsVoidResultBody` (`:853-862`, called at `:825-826` and `DomainEntityInstance.HostAbi.cs:328`) tacks on a trailing `return DomainResult.Success()` so the VM types the block as `DomainResult`.

Printed trees throw a dedicated `ConstraintFailureException` (new, does not subclass `InvalidOperationException`). The host around `Interpreter.Execute` (`ExecuteEffectList` `:839`, `ExecuteCachedSubscriptionTree` `:333`) catches only that type and returns `DomainResult.Failure`. Delete the BindForSimulate throw arm and `AsVoidResultBody`. `ThrowIfEffectListFailed` (`:719-723`) rethrows that dedicated type so void simulate (create / stage entry / subscription) matches printed ctor/handler throws.

Do not touch the remaining `BindForSimulate` adapter arm (`:990-995`, C1c), factory `ValidateConstraints` (`:136-137`, C2b known gap), or `NoteAutomaticStage` printed twin (`DomainToCSharpExporter.Actions.cs:1040-1046`, stays `System.InvalidOperationException`).

## Files
- `Poly/DomainModeling/Runtime/ConstraintFailureException.cs` (new; `Exception` subclass, string message ctor)
- `Poly/DomainModeling/Runtime/DomainResultTypeProvider.cs` (`:16-18`: also resolve `"ConstraintFailureException"` to the CLR type, same wrap `ModuleAwareTypeProvider` already uses at `:867`)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` (delete `AsVoidResultBody` and the IOE throw arm; catch dedicated type in `ExecuteEffectList`; `ThrowIfEffectListFailed` throws it)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` (drop `AsVoidResultBody` at `:328`; catch dedicated type in `ExecuteCachedSubscriptionTree`)
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (`:434-436` and `:1136-1140`: `NamedTypeReference("ConstraintFailureException")`)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs` (emit the exception type next to `BuildDomainResultTypeDef` `:631`, same pattern)
- `Poly/DomainModeling/Lowering/DomainProgramProjection.cs` (`:117-119`: add the exception type beside DomainResult)
- `Poly.Tests/DomainModeling/Compile/C1bConstraintFailureTests.cs` (new)
- `Poly.Tests/DomainModeling/Lowering/OnEntryConstraintAgreeTests.cs` (assert dedicated type on both sides)
- `Poly.Tests/TestHelpers/ParityScenario.cs` (`Record` `:129`: treat `ConstraintFailureException` as an outcome, same as IOE)
- `Poly.Tests/DomainModeling/Compile/EmitGolden/**/Poly.Types.cs.golden` (new type); `crm/Opportunity.cs.golden` (void OnEntry/ctor throws); `mcp-library/Patron.cs.golden:89` (void create fail-closed)

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Void OnEntry/ctor assign breaks range | Tree throws IOE; BindForSimulate rewrites to `return Failure`; `AsVoidResultBody`; `ThrowIfEffectListFailed` throws IOE. Printed ctor throws IOE. | Tree throws `ConstraintFailureException`; no rewrite; host catches only that → Failure; `ThrowIfEffectListFailed` throws `ConstraintFailureException`. Printed ctor throws `ConstraintFailureException`. Same message. | `OnEntryConstraintAgreeTests.OnEntryRangeViolation_FailsClosed_OnSimulateAndPrintedCsharp`; `ParityTests.Create_WhenEntryEffectBreaksRange_FailsTheSameWay` |
| Action assign breaks range | `ActionResultType` set: `return DomainResult.Failure` (no throw) | unchanged Failure on both sides | `ParityTests.Invoke_WhenAssignBreaksRange_FailsTheSameWay`; `ConstraintAssignLoweringTests.InvokeAction_RangeOutOfBounds_FailsAndLeavesPriorValue` |
| Void create-job fail-closed (subscription handler) | `throw new InvalidOperationException(create0.ErrorMessage ?? "")` | `throw new ConstraintFailureException(...)` | golden `mcp-library/Patron.cs.golden`; `C1bConstraintFailureTests` body still contains `ThrowStatement` of that type |
| Host fail-loud during Execute (automatic loop, wrong-typed SetArgs, Domain-null) | IOE escapes `Interpreter.Execute` | still IOE; `ExecuteEffectList` does not catch `InvalidOperationException` | `ExitBlockTransitionTests.Invoke_GuardedCycleThatKeepsHolding_FailsLoudOnBothSides`; `C1aRootProgramParamsTests.C1a_WrongTypedActionArgs_FailLoudLikeBagMemberReads`; new `C1bConstraintFailureTests` row that InvokeAction of a loop still throws IOE (not Failure) |
| `AsVoidResultBody` / BindForSimulate IOE arm | present (`DomainEntityInstance.cs:853`, `:1019`) | gone | `git grep AsVoidResultBody` empty; `C1bConstraintFailureTests` OnEntry body is still a throw, not a `DomainResult.Failure` Return |
| Factory Create out of range (C2b) | simulate throws IOE from `ValidateConstraints`; printed Create returns Failure | unchanged | `ParityTests.KnownGap_CreateOutOfRange_SimulateThrowsAndPrintedReturnsFailure` stays red-on-purpose |

## Done when
- Void constraint/create-fail-closed trees throw `ConstraintFailureException`; simulate and print agree on Failure/throw type and message (`OnEntryConstraintAgreeTests` plus `ParityTests.Create_WhenEntryEffectBreaksRange_FailsTheSameWay`).
- Action-body constraint Failure is unchanged (`Invoke_WhenAssignBreaksRange_FailsTheSameWay`).
- A host fail-loud IOE still escapes Execute (test); `Interpreter.Execute` and `ExecuteEffectList` do not `catch (InvalidOperationException)`.
- `git grep AsVoidResultBody` is empty; BindForSimulate IOE throw arm is gone; adapter arm remains.
- Emit goldens updated; full suite green.
- C2b `KnownGap_CreateOutOfRange_*` is not flipped.

## SHIP if / NOT SHIP if
SHIP if a host fail-loud throw is still not swallowed (test) and `git grep AsVoidResultBody` is empty. NOT SHIP if Execute (or `ExecuteEffectList` / `ExecuteCachedSubscriptionTree`) catches `InvalidOperationException` broadly, if `ConstraintFailureException` subclasses `InvalidOperationException`, or if the C2b factory-create known gap is quietly flipped.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/C1bConstraintFailureTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/OnEntryConstraintAgreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ConstraintAssignLoweringTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ExitBlockTransitionTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/C1aRootProgramParamsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
One small exception class, two `NamedTypeReference` sites, one `catch` around Execute, and a pure deletion of the rewrite helper; Scot can open those files cold.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-09 | planner | grok/grok-4.6 | `b1fddf7e` | planned | - |
