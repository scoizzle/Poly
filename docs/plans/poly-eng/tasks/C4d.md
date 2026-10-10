# TASK C4d - Stage transition table as a tree
Status: planned. Branch slice/c4d-transition-table. Lane B. Review 2. Implement mill: Grok (review on OpenCode).

## Scope
Depends on C4c (#141 `cc5cd55a`, on master) and decision V5 = a (decisions file `:91-106`, answered). The card's `HostAbi.cs:85-87` is stale: the unknown-stage silent no-op is `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:105-106`; the tree twin is `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:571-573` (`return new Block([])`). Unknown stage names are already Analyze errors (`EffectAnalyzer.ValidateStageTransition` `:840-848`, C4e #104) and `DomainProgramProjection.ToSyntax` refuses errors (`:18-22`), so this is fail-closed, not a new diagnostic. The same-stage return (`HostAbi.cs:109-110`) is idempotent and stays.
The table source (V5 = a) already exists; no new type: `CapabilityAnalyzer.AnalyzeAction` publishes real-`Stage` `ActionCapabilityView.TransitionTargets` (`CapabilityAnalyzer.cs:83-105`), and `BehaviorMetadata.From` maps them to `BehaviorAction.Transitions` with the declaring `StageName` (`BehaviorMetadata.cs:66-72`). State this in the PR.
The only red legal-transition parity row is the first-stage entry transition: `ApplyInitialStageEntryEffects` (`DomainEntityInstance.cs:324-339`) filters `StageTransitionEffect` (`:329-331`), so simulate stays in A while the printed ctor (`DomainToCSharpExporter.cs:690-714`) runs it to B (`ParityTests.KnownGap_FirstStageEntryTransition_SimulateStaysInFirstStage` `:749-755`). Run the first stage's `OnEntryEffects` through the existing `RunTransitionEffectList` (`HostAbi.cs:191-232`) instead of filtering. Keep the `HostAbi.cs` edit to one guard: C6a and C3a also touch that file.

## Files
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` (`TransitionStage` `:104-174`: replace the `:105-106` return with a throw; keep `:109-110`)
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (`StageTransition` `:570-671`: replace `:571-573` with the same throw)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` (`ApplyInitialStageEntryEffects` `:324-339`: call `RunTransitionEffectList` on `firstStage.OnEntryEffects`)
- `Poly.Tests/DomainModeling/Lowering/StageTransitionHostAbiTests.cs` (unknown-stage throws in the host and the lowering, one shared message; existing legal rows unchanged)
- `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` (flip `KnownGap_FirstStageEntryTransition_SimulateStaysInFirstStage` `:749-755` to `AssertAgree`; keep `Invoke_WhenActionTransitions_AgreesOnStageAndProperties` `:305-314`)
- Named and left alone: `DomainToCSharpExporter.cs:690-714` (ctor already lowers the transition), `RuntimeAnalysisCache.CacheEntryExitSegments` `:262-295` (mixed segments already cached), `CapabilityAnalyzer` / `BehaviorMetadata` (read-only source), `EffectAnalyzer` (C4e owns the diagnostic)

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `TransitionStage("Nope")` (unknown stage, declared entity) | silent `return` (`HostAbi.cs:105-106`) | `throw InvalidOperationException` (`Stage 'Nope' does not exist on entity 'X'.`) | `StageTransitionHostAbiTests.TransitionStage_UnknownStage_ThrowsFailClosed` |
| Lower an unknown-stage `StageTransitionEffect` | `return new Block([])` (`EffectLoweringPass.cs:571-573`) | same throw, same message | `StageTransitionHostAbiTests.Lower_UnknownStageTransition_ThrowsFailClosed` |
| `TransitionStage` to the current stage | early `return` (`HostAbi.cs:109-110`) | unchanged (idempotent, not unknown/illegal) | `DomainEntityInstanceTests` transition tests unchanged |
| First-stage entry `transition to B` (legal) | simulate filters the ST (`DomainEntityInstance.cs:329-331`), stays in A; print goes to B | both end in B | `ParityTests.Invoke_FirstStageEntryTransition_SimulateAndPrintedAgree` (replaces the `KnownGap_`) |
| First-stage entry `assign; transition; assign` (legal) | simulate drops the transition and the trailing segment | segments flush around `TransitionStage`; both sides agree | `ParityTests` mixed first-stage row |
| Action `Close` transitions Open->Closed (legal) | both agree | unchanged | `ParityTests.Invoke_WhenActionTransitions_AgreesOnStageAndProperties` |

## Done when
- `TransitionStage` and `EffectLoweringPass.StageTransition` both fail loud with one message for a target stage not declared on the entity; no `return` / empty block remains for an unknown stage.
- The table source is stated in the PR (`StageTransitionEffect` -> `ActionCapabilityView.TransitionTargets` -> `BehaviorAction.Transitions`); no new type.
- A first-stage entry transition runs on simulate and the parity row `AssertAgree`s (both end in B); the legal action-transition row and `ExitBlockTransitionTests` stay green.
- Full suite green; the printed ctor and the `VmAnalyzerReportTests` VM-error probe list are unchanged.

## SHIP if / NOT SHIP if
SHIP if an unknown-stage transition throws in both the host and the lowering with one message, the first-stage entry-transition parity row agrees, and the table source is stated.

NOT SHIP if any silent no-op / empty block remains for an unknown stage, if a new table type is introduced, if the printed ctor signature changes, if a `TransitionStage` caller test is weakened, or if `entry-transition-in-first-stage.poly` leaves the VM-error list.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StageTransitionHostAbiTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ExitBlockTransitionTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SliceBLowerAtLowerTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SliceASharedModuleBodyTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Item3LowerPopulateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainInstanceStoreFailClosedTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmAnalyzerReportTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
Three spots: one guard in `TransitionStage`, one guard in `EffectLoweringPass.StageTransition` (same message), and one call in `ApplyInitialStageEntryEffects` that reuses `RunTransitionEffectList`; each reads on its own, and the `HostAbi.cs` edit is a single guard so it does not collide with C6a / C3a.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-09 | planner | opencode-go/deepseek-v4.1-flash | `cc5cd55a` | planned | - |
