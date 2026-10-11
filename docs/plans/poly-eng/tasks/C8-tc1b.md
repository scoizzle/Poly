# TASK C8-tc1b - create-in lowers to `this.Create{Nav}` (G1)

Status: planned (blocked on C8-tc1a). Branch `slice/c8-tc1b`. Lane B. Review 2.
Split from C8-tc1 on 2026-10-10 by the local agent. Parent: [`tasks/C8-tc1.md`](C8-tc1.md).
Plan card: [`pipeline-convergence-plan.md` **C8-tc**](../../../domain-modeling/pipeline-convergence-plan.md).

## Scope
Finding G1 from [`research/2026-10-10-codegen-review.md`](../research/2026-10-10-codegen-review.md).
`create in` still lowers to `this.CreateIn("enrollments", …)` plus an
`(Enrollment)` cast (`CreateEntityInRelationship`,
`Poly/DomainModeling/Lowering/EffectLoweringPass.cs:909`, call `:917`). This
slice lowers it to the typed `this.CreateEnrollments(offering)` that the printed
C# already has (`DomainToCSharpExporter.Notify.cs`, `AddCreateNavMethod`), and
routes the interpreter call back onto the dictionary `CreateIn` job.

Depends on C8-tc1a for the shared plumbing: `internal GetConstructorParameters`,
`TrailingDefaultedProperties`, `EmptyListNode`, `TypedNull`, and the ambient
subject the interpreter uses for typed factories.

## Files
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (`CreateEntityInRelationship` lowers to `this.Create{Nav}(…)`; `LowerCreateInProbe` may switch to the typed `Type.ProbeCreate` only if it does not pull in C8-tc2)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` (add `internal static List<string> CreateNavArgumentNames(source, target, metadata)`)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` (add `TryTypedCreateNav(name, args, out result)`: pack positional `Create{Nav}` args into the dictionary `CreateIn` job)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.InvokeNamed.cs` (route `Create{Nav}` names to `TryTypedCreateNav`)
- Tests: `StoreBindCreateTests.cs`, `DomainToCSharpExporterTests.cs`, `ParityTests.cs`; goldens regenerated.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `create in enrollments { … }` | `this.CreateIn("enrollments", …)` + `(Enrollment)` cast | `this.CreateEnrollments(offering)` | `StoreBindCreateTests`; `university/Student.cs.golden` |
| same scenario simulated vs printed | agree | still agree | `ParityTests` |

## Done when
- No golden contains a string-named `this.CreateIn("` call; `university/Student.cs.golden` shows `this.CreateEnrollments(offering)` (also Guest.BookStay, Warehouse.RegisterTruck, Account.PlaceHq, Patron.CheckOut, Opportunity.Log/AddLine).
- The interpreter runs the typed `Create{Nav}`; `ParityTests` green. Goldens regenerated, not hand-edited. Full suite green.

## SHIP if / NOT SHIP if
SHIP if `create in` lowers to `this.Create{Nav}(...)`, parity holds, and no probe/`EnsureUnique` change rode along.

NOT SHIP if any `create in` still lowers string-named, a golden was hand-edited, or a probe/`EnsureUnique` change rode along (C8-tc2).

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindCreateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
POLY_UPDATE_GOLDEN=1 dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
One lowering switch from string job to `Create{Nav}`, and one name map in `InvokeNamed`; Scot can open both cold.

## Needs Scot
none

## Depends
C8-tc1a.

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | handoff | - | - | split from C8-tc1 | G1 |
