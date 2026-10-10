# TASK C8-clean - Codegen cleanups
Status: planned. Branch slice/c8-clean-codegen. Lane B. Review 2. Implement mill: Grok (review on OpenCode).
## Scope
Findings G11, G15, G16, G18, G19, G20, G22, D7, D9, D11, D13, D14, D18. Depends on C8-lc (same `EffectLoweringPass.cs`, `DomainToCSharpExporter.cs`, `Notify.cs`). Printed C# bar: nits included, nothing deferred.
G11 `warehouse/Warehouse.cs.golden:137`: Zip is not `required` in the DSL but `Create` always runs `^\d{5}$`, so empty Zip fails. Same for Doctor.LicenseNo (pattern, no required). Skip pattern/length when the property is optional and the value is default/empty (`BuildCreateConstraintChecks` `DomainToCSharpExporter.Notify.cs`).
G15 `orders/Customer.cs.golden:67`: for-invoke skip is `if (!(target0.CurrentStage == OrderStage.Pending))`. Emit `!=` (`DomainExpressionLoweringPass.cs`, `Not` of `Equal`). Also clinic/Patient, university/Student (2), crm/Opportunity.
G16 `warehouse/Truck.cs.golden:29`: membership checks on values already typed as the enum (`assignValue0 != Idle && != EnRoute && …`) in ctor entry, `SetStatus`, `Order.Create` State, Visit.CheckIn. Skip enum-membership guards when the CLR type is already that enum (`EffectLoweringPass.cs:430`; `BuildCreateConstraintChecks`).
G18 `university/Student.cs.golden:194`: `Enrollments.Count > 0L` (int vs long). Same `> 0L` / `== 0L` in Guest.HasStays, Section.HasWaiters, simulate-create HasFineCount/NoFines. Compare count to `0` (`DomainExpressionLoweringPass.cs`).
G19 `orders/Order.cs.golden:109`: `Order.Create(string cancelReason, long discount, long total, …)` puts later-assigned `CancelReason` first; every new order must pass a cancel reason. Delivery.Confirm/`Notes` similar. Optional/default empty for assign-only props; required create props first (`EntityStructureAnalyzer.cs` ctor param order via `Notify.cs`).
G20 `hotel/Folio.cs.golden:81`: `Settle` assigns `0L` through `assignValue0` plus range checks that cannot fail; extra brace blocks around entry assigns (Opportunity.Propose, Visit.CheckIn). Skip constraint codegen when the RHS is a constant already inside range (`EffectLoweringPass.cs` assign constraint wrap).
G22 `clinic/Doctor.cs.golden:33`: `StartShift` null-guard says `'Open' requires a linked 'schedule' on entity 'Doctor'` (the target action name). Use the calling action name (`StartShift`) (`EffectLoweringPass.cs:699`).
D7 `orders/Poly.Types.cs.golden:32`: `DomainResult<T>` ctor and `Success` use a needless verbatim identifier `this.Value = @value;`. Drop the `@` (`DomainProgramProjection.cs`).
D9 `orders/OrderItem.cs.golden:7`: a `private OrderItem() { }` (or `private Order() { _items = new List<OrderItem>(); }`) parameterless ctor is emitted for EF materialization even in domains that never use EF. Two shapes (`{}` vs list-init) are inconsistent. Emit the EF ctor only when a persistence pack is present; make its body consistent (`DomainToCSharpExporter.cs:736`).
D11 `hotel/Room.cs.golden:139`: two `when stays Departed` blocks collapse to `WhenEachReservationDeparted` and `WhenEachReservationDeparted_2`. Name handlers after their effect/target (`BuildHandlerNames` `DomainToCSharpExporter.cs:102`).
D13 `clinic/Visit.cs.golden:55` (also university/Section.cs.golden `WhenAnyWaitOfferOffered`/`WhenAllWaitOfferCancelled`): entry/`when` effect bodies wrapped in a redundant bare `{ … }` around a single assignment. Drop the extra block (`EffectLoweringPass.cs`).
D14 `university/Enrollment.cs.golden:76`: `RecordGrade` else branch assigns literal `0L` then re-checks `0L` against `range(0,100)` (always true); same for `OnEntryEnRoute` re-checking `TruckStatus.EnRoute`. Skip range/enum validation when the RHS is a compile-time constant already within bounds (`EffectLoweringPass.cs:292`). Same family as G20.
D18 `orders/Order.cs.golden:2`: every emitted file carries `using System;` but nothing references an unqualified `System` member. Emit only the usings the body actually needs (`DomainProgramProjection.cs`).
## Files
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` (G11 `BuildCreateConstraintChecks`; G16; G19 param order)
- `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` (G15 `!=`; G18 count `0`)
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (G16 `:430`; G20 assign wrap; G22 `:699`; D13 extra block; D14 `:292`)
- `Poly/DomainModeling/Analysis/EntityStructureAnalyzer.cs` (G19 ctor param order)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` (D9 `:736` EF ctor; D11 `:102` `BuildHandlerNames`)
- `Poly/DomainModeling/Lowering/DomainProgramProjection.cs` (D7 `@value`; D18 usings)
- `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs`, `Poly.Tests/DomainModeling/Compile/EmitGoldenTests.cs`
- Goldens: regenerate `Poly.Tests/DomainModeling/Compile/EmitGolden/**` (none hand-edited)
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Optional Zip / LicenseNo on create (G11) | pattern always runs; empty Zip fails | skip pattern/length when optional and default/empty | G11 `warehouse/Warehouse.cs.golden:137` |
| For-invoke stage skip (G15) | `if (!(target0.CurrentStage == OrderStage.Pending))` | `if (target0.CurrentStage != OrderStage.Pending)` | G15 `orders/Customer.cs.golden:67` |
| Enum-typed assign (G16) | membership walk `!= Idle && != EnRoute && …` | no membership guard when the CLR type is that enum | G16 `warehouse/Truck.cs.golden:29` |
| `Enrollments.Count > 0L` (G18) | compare to `0L` | compare to `0` | G18 `university/Student.cs.golden:194` |
| `Order.Create(cancelReason, …)` (G19) | assign-only `CancelReason` is first and required | required create props first; assign-only optional/default empty | G19 `orders/Order.cs.golden:109` |
| `Settle` assigns `0L` (G20) / `RecordGrade` else `0L` (D14) | range/enum re-check of a constant already in bounds | skip the wrap | G20 `hotel/Folio.cs.golden:81`; D14 `university/Enrollment.cs.golden:76` |
| `StartShift` null-guard (G22) | message names target action `Open` | message names calling action `StartShift` | G22 `clinic/Doctor.cs.golden:33` |
| `this.Value = @value` (D7) | needless verbatim identifier | `this.Value = value` | D7 `orders/Poly.Types.cs.golden:32` |
| Parameterless entity ctor (D9) | always emitted, two body shapes | only with a persistence pack; consistent body | D9 `orders/OrderItem.cs.golden:7` |
| Two `when stays Departed` (D11) | `WhenEachReservationDeparted_2` | name after effect/target (e.g. `…_MarkDirty`) | D11 `hotel/Room.cs.golden:139` |
| Entry/`when` single assign (D13) | extra `{ … }` block | no extra block scope | D13 `clinic/Visit.cs.golden:55` |
| `using System;` (D18) | emitted in every file, unused | only usings the body needs | D18 `orders/Order.cs.golden:2` |
## Done when
- Each cited golden no longer shows its finding. Goldens regenerated, not hand-edited. Full suite green.
## SHIP if / NOT SHIP if
SHIP if each cited golden no longer shows its finding and the suite passes with regenerated goldens.

NOT SHIP if optional Zip still fails create, or any listed nit is deferred.
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
Each finding is a local emit edit (skip a guard, flip `!=`, drop a using, rename a handler); Scot can open those spots cold.
## Needs Scot
none
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | grok/grok-4.6 | c0bd182b | planned | G11 G15 G16 G18 G19 G20 G22 D7 D9 D11 D13 D14 D18 |
