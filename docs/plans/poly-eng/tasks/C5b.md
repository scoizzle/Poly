# TASK C5b - Multi-hop, depth and fan-out leave the store
Status: in review. Branch slice/c5b-plan. Lane B. Review 2. Implement mill: Grok (review on OpenCode).

## Scope
Depends on C5a (#149 `fa994ff7`, on master) and decision 9 / V2 (storage-only, answered: `pipeline-convergence-plan.decisions.md:33-50`); no open decision.
After C5a simulate still fans out by *matching* in the store: `NotifyTransition` (`Poly/DomainModeling/Runtime/DomainInstanceStore.cs:433-526`) scans `_instances`, matches each candidate's `SubscriptionDispatchPlanMetadata` against `RelationshipContractMetadata` + the target stage, checks `IsLinked` (`PlanListsSubscriber` `:532-560`, `WriteSubscriberRegistry` `HostAbi.cs:260-264`), runs the compiled body, then *recurses* under a silent `maxDepth = 10` (`:438-439`, `:515-516`).
Print stops after one hop because the subscription-handler lowering builds its `LoweringContext` without `PostTransitionNotifyStages` (`DomainToCSharpExporter.cs:564-569`; the set exists at `:396-401`), so a `transition` inside a `when` handler emits no `Notify{Target}Subscribers` (`EffectLoweringPass.cs:570,578-585,653-657`); the gap is `ParityTests.KnownGap_SubscriberTransitionCascade_PrintedStopsAfterOneHop` (`Poly.Tests/DomainModeling/Lowering/ParityTests.cs:948-968`, owner C5b).
C5b makes the compiled handler tree drive the cascade and moves registration out of notify time. The initial `files: DomainInstanceStore.cs` card line is stale: parity needs the exporter (and the `_isExecutingSubscription` suppression in HostAbi) too.

## Files
- `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` — move subscriber registration to `Link`/`Unlink`/`Remove` (`:336-371`); shrink `NotifyTransition` (`:433-526`) to a catalog-guarded `ExecuteNotifyStageSubscribers` call (no `_instances` scan, no `depth`/`maxDepth`, no recursion); delete `PlanListsSubscriber` (`:532-560`).
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` — drop the `_isExecutingSubscription` suppression in `Notify` (`:26-29`) and `TransitionStage` (`:166`); replace `WriteSubscriberRegistry`/`ClearSubscriberRegistries` (`:260-273`) with link-time add/remove (or delete if unused). `ExecuteNotifyStageSubscribers` (`:280-303`) stays.
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` — delete `_isExecutingSubscription` (`:22`).
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` — pass `PostTransitionNotifyStages: postTransitionNotifyStages` into the handler `LoweringContext` (`:564-569`); print and the simulate module body share this code, so one edit fixes both.
- `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` — flip `KnownGap_SubscriberTransitionCascade_PrintedStopsAfterOneHop` (`:948-968`) to `AssertAgree`; add the multi-hop rows.
- `Poly.Tests/DomainModeling/Analysis/DomainInstanceStoreFailClosedTests.cs` — retarget the notify-time metadata checks (`:46-128`, `:142-183`) at link-time registration.
- `Poly.Tests/DomainModeling/Compile/WhenAnySimulatePrintAgreeTests.cs` — keep the registry-from-links assertion (`:86-92`).

## Shape matrix
| input kind | before | after | test that proves it |
|---|---|---|---|
| subscriber cascade (`Paper A->B` -> `Tr P->Q` -> `W P->R`) | simulate recurses in the store to R; print stops at P (`:948-968`) | both reach R: the compiled `Notify{Target}Subscribers` call in the handler drives each hop | `ParityTests` multi-hop `AssertAgree` row (replaces the `KnownGap_`) |
| a `transition` inside a `when` handler | handler lowering omits `PostTransitionNotifyStages` (`:564-569`) -> no fan-out call | handler body emits `Notify{Target}Subscribers` like an action (`EffectLoweringPass.cs:653-657`) | the multi-hop row; `WhenAny/WhenAllSimulatePrintAgreeTests` stay green |
| transition beyond depth 10 | store `return`s silently (`:438-439`, `:515-516`) | no depth cap; loops fail loud via the automatic-stage guard (`HostAbi.cs:113-116`) | multi-hop row; `DomainEntityInstanceTests` loop-guard rows |
| notify with a linked subscriber | store scans `_instances`, matches plan+contract+link+stage, fills registry (`:532-560`) | registry filled when the link is made (`Link` `:348-358`); `NotifyTransition` only runs the compiled body | `WhenAnySimulatePrintAgreeTests:86-92`; retargeted `DomainInstanceStoreFailClosedTests` |
| unlink a subscriber, then notify | registries rebuilt each notify, so the unlinked subscriber drops out | `Unlink` removes the subscriber from the registry | new `StageNotifyDispatchTests` row |

## Done when
- `NotifyTransition` scans no instances and matches no link, contract or stage; it runs the transitioned instance's compiled `Notify{Stage}Subscribers` body (`HostAbi.cs:280-303`) and does nothing else.
- Subscriber registries are maintained by `Link`/`Unlink`/`Remove`, not cleared/refilled at notify time.
- A `transition` inside a `when` handler emits `Notify{Target}Subscribers`; the multi-hop parity row agrees (both sides reach R).
- No `maxDepth`/silent return remains; loops fail loud via the existing automatic-stage guard.
- `git grep -n -e PlanListsSubscriber -e maxDepth Poly/DomainModeling/Runtime/DomainInstanceStore.cs` is empty; full suite green.

## SHIP if / NOT SHIP if
SHIP if `NotifyTransition` contains no link matching (no `IsLinked`, no dispatch-plan/contract scan, no `maxDepth`) and the multi-hop parity row `AssertAgree`s in both simulate and print.

NOT SHIP if a cascade can stop silently, if the printed body and the simulate cached body differ, if `_isExecutingSubscription` still suppresses the handler's fan-out, or if the registry is rebuilt by matching at notify time.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/WhenAnySimulatePrintAgreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/WhenAllSimulatePrintAgreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StageNotifyDispatchTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainInstanceStoreFailClosedTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
Four reads: one `PostTransitionNotifyStages` pass in the exporter, the handler invocation moved into `Link`/`Unlink`, and a `NotifyTransition` shrunk to run the compiled body with `_isExecutingSubscription` gone; each stands alone and the multi-hop parity row shows the behavior.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | opencode-go/deepseek-v4.1-flash | fa994ff7 | planned | card's Files list omits the exporter/HostAbi edits print parity needs; store still matches + caps depth after C5a |
| 2026-10-10 | implementer | grok/grok-4.6 | 9221666f | pushed | tests 3516/3516; sweep: handler Notify drives cascade; unlink drops registry |
| 2026-10-10 | reviewer | opencode/opencode-go/deepseek-v4.1-flash | 8f4022b5 | NOT SHIP | https://github.com/scoizzle/Poly/pull/152#issuecomment-6100249079 (Grug NOT SHIP at 8f4022b5, 4 open findings, mode full) |
| 2026-10-10 | implementer | grok/grok-4.6 | 01711d52 | fixes pushed | R1, R2, R3, R4; disputed: none; tests 3525/3525 |
