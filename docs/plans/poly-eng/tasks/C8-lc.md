# TASK C8-lc - Lifecycle in printed C#
Status: planned. Branch slice/c8-lc-lifecycle. Lane B. Review 2. Implement mill: Grok (review on OpenCode).
## Scope
Findings G5, G7, G10, G17, G21, D10, D12. Depends on C8-tc (same `DomainToCSharpExporter.cs`, `Actions.cs`, `EffectLoweringPass.cs`).
G5 `warehouse/Truck.cs.golden:26`: ctor sets `CurrentStage` to the first declared stage (`EnRoute`) and runs its entry (`Status = EnRoute` plus a tautological enum check that `throw`s). New trucks start EnRoute while `Status` defaults to Idle in the DSL. Same first-stage+entry pattern on Opportunity (Qualify assigns Probability and can throw) and every staged entity. `DomainToCSharpExporter.cs:702`: pick an explicit initial stage (Idle/Pending); run entry only on transition; never throw from `Create`.
G7 `orders/Order.cs.golden:108`: `IsPaid` reads `State == OrderState.Paid`; `Pay`/`Ship`/`Cancel` only assign `CurrentStage` (`OrderStage`). After `Pay()`, `ShipAll` matches zero orders. Dual enums `OrderState` (Poly.Types) vs `OrderStage` (Order.cs). Visit keeps them in sync only because CheckedIn has `entry { assign State }`; Order.Paid does not. One lifecycle type, or assign the domain enum on every transition (`DomainToCSharpExporter.cs` stage enum + `EffectLoweringPass.cs` transition).
G10 `crm/Opportunity.cs.golden:253`: stage copies of `Log`/`AddLine`/`Lose` print the same body three times (Qualify/Propose/Commit). Hotel `Reservation.Cancel`/`EventHold` similar. `AddStageDispatchedActionMethod` (`DomainToCSharpExporter.Actions.cs`): when stage copies share effects, emit one body with a stage-set guard.
G17 `orders/Order.cs.golden:66`: entity-level `Ship()` assigns `CurrentStage = Shipped` from any stage, including Cancelled. `Pay` is Pending-guarded. Visit.CheckIn is the same shape. Guard entity-level transitions with the legal source stages from the lifecycle graph (`DomainToCSharpExporter.Actions.cs`).
G21 `simulate-create-in/Patron.cs.golden:5`: single-member `PatronStage { Active }` and a `CurrentStage` field for a one-stage entity. Also simulate-create-type and simulate-create-create-in Patron. Omit the stage enum when there is only an implicit initial stage (`DomainToCSharpExporter.cs:681`).
D10 `university/Student.cs.golden:643` (also clinic/Visit.cs.golden:468, warehouse/Truck.cs.golden:488, crm/Opportunity.cs.golden:1140–1192): `OnEntry*` handlers are dead — the `entry { assign … }` effect is inlined into the transitioning action (`Suspend` sets `Holds+1`; `CheckIn` sets `State`; `Dispatch` sets `Status`), so `private void OnEntrySuspended()` etc. are never called and duplicate the logic. Emit and call a single `OnEntry{Stage}()` helper from every transition into that stage, or stop emitting the orphaned helper (`EffectLoweringPass.cs` entry inlining + `DomainToCSharpExporter.cs:713`).
D12 `hotel/Reservation.cs.golden:259`: `previousStage` on `Notify{InHouse,Departed,Cancelled,NoShow}Subscribers(ReservationStage previousStage)` is unused in the `when each`/`when any` cases (only `when all` consumes it). Only thread `previousStage` into the `when all` notify path (`DomainToCSharpExporter.cs:499/540`).
## Files
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` (G5 `:702`, G21 `:681`, D10 `:713`, D12 `:499`/`:540`, G7 stage enum)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs` (G10 `AddStageDispatchedActionMethod`, G17 entity-level transition)
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (G7 transition, D10 entry inlining)
- `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs`, `Poly.Tests/DomainModeling/Lowering/ParityTests.cs`, `Poly.Tests/DomainModeling/Compile/EmitGoldenTests.cs`
- Goldens: regenerate `Poly.Tests/DomainModeling/Compile/EmitGolden/**` (none hand-edited)
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| New Truck / Opportunity `Create` (G5) | ctor sets first declared stage and runs entry (EnRoute + throw; Qualify assigns Probability) | explicit initial stage; entry only on transition; `Create` never throws | G5 `warehouse/Truck.cs.golden:26`; parity create of a staged entity |
| `Order.Pay` then `ShipAll` (G7) | `IsPaid` reads `OrderState`; `Pay` assigns `OrderStage`; `ShipAll` matches zero | one lifecycle type, or domain enum assigned on every transition | G7 `orders/Order.cs.golden:108` |
| Stage copies of `Log`/`AddLine`/`Lose` (G10) | same body printed three times (Qualify/Propose/Commit) | one body with a stage-set guard | G10 `crm/Opportunity.cs.golden:253` |
| Entity-level `Ship()` from Cancelled (G17) | assigns `Shipped` from any stage | guarded by legal source stages from the lifecycle graph | G17 `orders/Order.cs.golden:66` |
| One-stage Patron (G21) | `PatronStage { Active }` + `CurrentStage` field | no stage enum when there is only an implicit initial stage | G21 `simulate-create-in/Patron.cs.golden:5` |
| `OnEntrySuspended` etc. (D10) | private helper never called; entry inlined in the action | one called `OnEntry{Stage}()` helper, or the orphan is not emitted | D10 `university/Student.cs.golden:643` |
| `NotifyInHouseSubscribers(previousStage)` (D12) | unused `previousStage` on `when each`/`when any` | `previousStage` only on the `when all` notify path | D12 `hotel/Reservation.cs.golden:259` |
## Done when
- Each cited golden no longer shows its finding.
- `Create` does not run first-stage entry or throw (G5). Order has one lifecycle, or the domain enum is assigned on every transition (G7). Shared stage bodies are not triplicated (G10). Illegal entity-level transitions fail (G17). Single-stage entities have no stage enum (G21). No dead `OnEntry*` (D10). No unused `previousStage` on non-all notifiers (D12).
- Goldens regenerated, not hand-edited. Full suite green.
## SHIP if / NOT SHIP if
SHIP if each cited golden no longer shows its finding and the suite passes with regenerated goldens.

NOT SHIP if Create still throws from entry, dual enums remain on Order, stage copies are still triplicated, or any listed finding is deferred.
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
Ctor/stage-enum emission, one dispatch helper, transition guards, and `previousStage` threading are local emit edits; Scot can open those spots cold.
## Needs Scot
none
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | grok/grok-4.6 | c0bd182b | planned | G5 G7 G10 G17 G21 D10 D12 |
