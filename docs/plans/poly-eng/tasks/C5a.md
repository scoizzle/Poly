# TASK C5a - Store fan-out runs the compiled Notify tree

Status: planned. Branch slice/c5a-plan. Lane B. Review 2. Implement mill: Grok (review on OpenCode).

## Scope
Depends on C3b, merged at `c3b9d9d3` (#148); C5a names no open decision (V10's note "C5a no longer depends on Q1a", `pipeline-convergence-plan.decisions.md:189`). The card's "known `when any` mismatch (>=1 vs exactly 1)" is stale: it was fixed by `76ab9346` (2026-09-27); `WhenAnySimulatePrintAgreeTests` (2/2) and `ParityTests` (62/62) are green on master `c3b9d9d3` (ran). What is still true is the *implementation split*: simulate fans out in `DomainInstanceStore.NotifyTransition` (`DomainInstanceStore.cs:441-507`) via `DispatchMatchingEntries` (`:509-558`) hand-matching relationship contracts (`:519-528`), `IsLinked` (`:532`) and `entry.StageNames` (`:536`); print fans out through the module's compiled `Notify{Stage}Subscribers` body (`DomainToCSharpExporter.cs:482-519`) which iterates the registry field `_{source}{stage}Subscribers` (`:44-45`) and calls `sub.When…` handlers. The transition tree already invokes `Notify{Target}Subscribers` (`EffectLoweringPass.cs:652-655`), but the VM reroutes that name to `DomainEntityInstance.Notify` → the store (`InvokeNamed.cs:75-91` → `HostAbi.cs:19-29`), so the simulated Notify body is store C#, not the compiled tree.

## Files
- `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` — `NotifyTransition` `:441-507`, `DispatchMatchingEntries` `:509-558`.
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` — `Notify` `:19-29`, `ExecuteSubscriptionEffects` `:266-323` (the cached-body executor); **overlaps C4d** (C4d edits this file for `TransitionStage`).
- `Poly/DomainModeling/Runtime/DomainEntityInstance.InvokeNamed.cs` — `InvokeNamed` `:21-68`, `TryNotifyStageSubscribers` `:75-91` (add the handler-name arm beside it).
- `Poly/DomainModeling/Runtime/DomainEntityInstance.Runtime.cs` — runtime type provider `BuildTypeDefNode` `:171-273`; the `Notify{Stage}Subscribers` stubs `:226-238` are why the compiled body cannot run today. Declare the registry member here only if the provider needs it.
- `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` — add the `when any`, `when all` and single-hop rows (`TrackingDsl`/`PeerReadDsl` `:509-592` show the shape).
- `Poly.Tests/DomainModeling/Compile/WhenAnySimulatePrintAgreeTests.cs` — pin that the registry the tree iterates is populated from links.

## Shape matrix
| input kind | before | after | test that proves it |
|------------|--------|-------|---------------------|
| stage transition with a `when any` subscriber linked to the target | store matches contract + link + stage and calls the cached handler (`Store.cs:509-558`) | the transitioned instance's compiled `Notify{Stage}Subscribers` body runs; it iterates the registry and calls `sub.WhenAny…` | `ParityTests` new `when any` row; `WhenAnySimulatePrintAgreeTests` |
| same, `when all` | store match then cached handler | same single compiled body | `ParityTests` new `when all` row; `WhenAllSimulatePrintAgreeTests` |
| single hop (`when Tracks B`), Each | store match then cached handler | compiled body | `ParityTests.Invoke_WhenTrackedPeerTransitions_SubscriberTransitionsToo` `:551` |
| simulate calls the handler by name | `InvokeNamed` resolves only Create/Notify/actions/policies; `When…` is not an action → not found | a `When…` arm routes to the cached subscription body (`TryGetSubscriptionBody`, `RuntimeAnalysisCache.cs:173`) | new simulate assertion in `WhenAnySimulatePrintAgreeTests` |
| subscriber cascade (multi-hop) | store recurses, print stops after one hop | unchanged — `KnownGap_SubscriberTransitionCascade_PrintedStopsAfterOneHop` (`ParityTests.cs:853`) stays owned by C5b | that test still passes |

## Done when
- On a transition, simulate executes the module's compiled `Notify{Stage}Subscribers` body (`DomainToCSharpExporter.cs:482-519`), not `DispatchMatchingEntries`' own match; the store's role is only to supply the linked-subscriber registry the body iterates.
- Parity `AssertAgree` rows pass for `when any`, `when all`, and a single hop (the card's three rows); full suite green.
- Print output is unchanged (exporter untouched); the C5b cascade gap is still a gap (do not fix it here).

## SHIP if / NOT SHIP if
SHIP if the transitioned instance's compiled `Notify{Stage}Subscribers` body is what runs in simulate (a test shows the handler firing with the registry populated from `_links`), all three parity rows `AssertAgree`, and `git diff` shows no printed-C# change.

NOT SHIP if print and simulate still differ on `any` or `all`, if `DispatchMatchingEntries` is merely renamed/left as the matcher, if the exporter or printed output changed, or if the C5b cascade gap is silently closed.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/WhenAnySimulatePrintAgreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/WhenAllSimulatePrintAgreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StageNotifyDispatchTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainInstanceStoreFailClosedTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
One fan-out call in `NotifyTransition`, one registry-population block, and one `When…` arm next to `TryNotifyStageSubscribers`; Scot can read the store change against the compiled body the probe printed (`if (this._patronOverdueSubscribers != null) foreach (var sub in …) sub.WhenAnyLoanOverdue();`).

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | opencode-go/deepseek-v4.1-flash | c3b9d9d3 | planned | card's `when any` red-row premise stale (`76ab9346`); store/tree split verified |
