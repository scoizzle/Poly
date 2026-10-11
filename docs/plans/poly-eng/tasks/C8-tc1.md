# TASK C8-tc1 - Lowering emits typed create calls
Status: planned (written for the local-agent handoff, 2026-10-10). Branch `slice/c8-tc1` (not created yet). Lane B. Review 2.
Plan card: [`pipeline-convergence-plan.md` **C8-tc**](../../../domain-modeling/pipeline-convergence-plan.md). C8-tc was split into tc1, tc2 and tc3 on 2026-10-10 after its run hung four times; this file is part 1 of 3.

## Scope
Findings K0 (lowering half) and G1 from [`research/2026-10-10-codegen-review.md`](../research/2026-10-10-codegen-review.md). Lowering still emits string-named store jobs: `LowerRuntimeFactoryCall` (`Poly/DomainModeling/Lowering/EffectLoweringPass.cs:1136`), reached from `CreateEntityInstance` (`:897`) and `CreateEntityInRelationship` (`:909`, call `:917`), prints `this.Create("Fine", "Amount", 5L, …)` and `this.CreateIn("enrollments", …)` with object casts. The typed factories already exist: static `Entity.Create(…)` (`DomainToCSharpExporter.cs`, near `:805`) and `Create{Nav}(…)` (`DomainToCSharpExporter.Notify.cs:92`, singular `:84`).
This slice makes create and create-in lower to those typed calls, returning `DomainResult<T>` with no cast to `object`. The interpreter maps the typed calls onto its dictionary-backed instances: `DomainEntityInstance.InvokeNamed` (`Poly/DomainModeling/Runtime/DomainEntityInstance.InvokeNamed.cs:26`) and `RuntimeCreateFactory` / `Create` / `CreateIn` (`DomainEntityInstance.HostAbi.cs:430`, `:492`, `:502`).
Out of scope (later parts): the probe (`LowerCreateInProbe` `:1104`, `LowerCreateEntityInstanceProbe` `:1122`) and `EnsureUnique` stay as they are until C8-tc2; `StoreBind.cs` is not deleted here.
- K0 `Poly.Tests/DomainModeling/Compile/EmitGolden/orders/Customer.cs.golden:111`: the compiled tree carries typed create calls; dictionaries stay in the interpreter only.
- G1 `Poly.Tests/DomainModeling/Compile/EmitGolden/university/Student.cs.golden:69`: `create in` lowers to `this.CreateEnrollments(offering)` (also Guest.BookStay, Warehouse.RegisterTruck, Account.PlaceHq, Patron.CheckOut, Opportunity.Log/AddLine).

## Files
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (create and create-in lowering)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.InvokeNamed.cs` (route `Create{Nav}` and `Entity.Create` names)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` (map typed positional args onto the dictionary-backed instance)
- Tests: `Poly.Tests/DomainModeling/Lowering/StoreBindCreateTests.cs`, `DomainToCSharpExporterTests.cs`, `ParityTests.cs`; goldens regenerated under `Poly.Tests/DomainModeling/Compile/EmitGolden/`.
- Reference: [`research/c8-tc-wip.patch`](../research/c8-tc-wip.patch) has a working draft of this lowering (`BuildFullCreateArgs`, `BuildCreateNavArgs`, `TryTypedCreateNav`). It applies cleanly at `931b1461`, not on current master (`DomainToCSharpExporter.cs` and `HostAbi.cs` conflict); use `git apply --3way` or copy hunks by hand.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `create Fine { Amount: 5 }` | `this.Create("Fine", "Amount", 5L)`, `DomainResult<object>`, cast | `Fine.Create(5L, …defaults)` returning `DomainResult<Fine>` | `StoreBindCreateTests`; golden `simulate-create-type/Fine.cs.golden` |
| `create in enrollments { … }` | `this.CreateIn("enrollments", …)` + `(Enrollment)` cast | `this.CreateEnrollments(offering)` | `StoreBindCreateTests`; G1 golden |
| Same scenario simulated vs printed | agree | still agree | `ParityTests` |

## Done when
- No golden contains a string-named `this.Create("…"` or `this.CreateIn("…"` call; G1's golden shows `CreateEnrollments(offering)`.
- The interpreter runs the typed calls; `ParityTests` green. Goldens regenerated, not hand-edited. Full suite green.

## SHIP if / NOT SHIP if
SHIP if `git grep -n 'CreateIn("' -- Poly.Tests/DomainModeling/Compile/EmitGolden` and `git grep -n 'this.Create("' -- Poly.Tests/DomainModeling/Compile/EmitGolden` are empty and parity holds.
NOT SHIP if any create or create-in still lowers string-named, a golden was hand-edited, or a probe/unique change rode along (that is tc2).

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindCreateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
POLY_UPDATE_GOLDEN=1 dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
One lowering switch from string job to typed call, and one name map in `InvokeNamed`; Scot can open both cold.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | handoff | - | - | planned from the C8-tc split | K0 G1 |
