# TASK C8-tc2 - Typed probe and real EnsureUnique; delete StoreBind.cs
Status: planned (written for the local-agent handoff, 2026-10-10). Branch `slice/c8-tc2` (not created yet). Lane B. Review 2.
Plan card: **C8-tc**; part 2 of 3. Depends on C8-tc1.

## Scope
Findings K0 (exporter half), G2, G3+D1 from [`research/2026-10-10-codegen-review.md`](../research/2026-10-10-codegen-review.md). `DomainToCSharpExporter.StoreBind.cs` (317 lines) backs the string-named jobs with `Dictionary<string,object>` overloads, `BindCreate` / `BindCreateIn` / `BindProbeCreate` and an always-Success `EnsureUnique` stub (`:26`); it is added by `AddStoreBindMethods` (`DomainToCSharpExporter.cs:878`). 48 golden files hold `new Dictionary<string, object`.
- K0 `Poly.Tests/DomainModeling/Compile/EmitGolden/orders/Customer.cs.golden:111`: delete `StoreBind.cs` and the `AddStoreBindMethods` call.
- G3+D1 `Poly.Tests/DomainModeling/Compile/EmitGolden/orders/Customer.cs.golden:110`: `EnsureUnique` is a real check. Lowering `WrapConstrainedAssign` (`EffectLoweringPass.cs:294`, call `:312`) and create's `BuildCreateUniqueChecks` (`DomainToCSharpExporter.Notify.cs:482`, call `:495`) emit a typed per-property check (the WIP patch uses `EnsureUnique{Prop}`); the interpreter maps it to `DomainInstanceStore.EnsureUnique` (`Runtime/DomainInstanceStore.cs:63`).
- G2 `Poly.Tests/DomainModeling/Compile/EmitGolden/university/Enrollment.cs.golden:228`: deleting `StoreBind.cs` removes `BindProbeCreate`, so the probe needs a typed home in this slice. Write it side-effect-free (constraint checks only; no construct, no attach), as the WIP patch's `BuildProbeCreateMethod` does. **Handoff change:** the requested split put G2 in tc3, but tc2 cannot delete `StoreBind.cs` without replacing the probe, so G2 moved here.
Note: the parent request also put G9 (public `Create{Nav}`) and G12 (`DomainResult<T>.Failure` value) in tc2. They were moved to tc3 to keep tc2 near the 3-source-file limit; both are in `Notify.cs` / `Actions.cs`, which tc3 already edits.

## Files
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.StoreBind.cs` (delete)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` (drop `:878`; typed probe method)
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (probe `:1104`/`:1122` and unique `:312` lowering)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` (`BuildCreateUniqueChecks` `:482`)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.InvokeNamed.cs`, `DomainEntityInstance.HostAbi.cs` (`EnsureUnique` `:35`, `ProbeCreate` `:512`), `Runtime/DomainInstanceHostJobs.cs`
- Tests: `StoreBindUniqueTests.cs`, `Item4FailBeforeMutateTests.cs`, `Runtime/DomainInstanceHostJobsTests.cs`, `Compile/EveryConceptHasTreeTests.cs` (unique detection keys on the method name), goldens.
- This is the largest part. If an agent stalls, split it: **tc2a** = real `EnsureUnique` (G3+D1), **tc2b** = typed probe + delete `StoreBind.cs` (G2, K0).

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| unique assign / unique on create | `EnsureUnique` stub `=> DomainResult.Success()` | real check; duplicate fails | `StoreBindUniqueTests`; G3+D1 golden |
| fail-before-mutate probe | `BindProbeCreate` calls `Enrollment.Create` (attaches) | constraint checks only, no construct or attach | `Item4FailBeforeMutateTests`; G2 golden |
| any golden | 48 files with `new Dictionary<string, object` | none | `git grep` below |

## Done when
- `StoreBind.cs` is gone; `git grep -n 'new Dictionary<string, object' -- Poly.Tests/DomainModeling/Compile/EmitGolden` and `git grep -n 'ProbeCreate("\|EnsureUnique("' -- Poly.Tests/DomainModeling/Compile/EmitGolden` are empty.
- A duplicate unique value fails in printed C# and in simulation; a failed probe leaves no child attached. Goldens regenerated. Full suite green.

## SHIP if / NOT SHIP if
SHIP if those greps are empty and the unique and probe rows are red on master and green here.
NOT SHIP if `EnsureUnique` is still a Success stub, the probe still constructs or attaches, or the compiled tree still builds a `Dictionary<string, object>`.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindUniqueTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Item4FailBeforeMutateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainInstanceHostJobsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EveryConceptHasTreeTests/*'
POLY_UPDATE_GOLDEN=1 dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
Mostly deletion; the new probe is a list of constraint checks with no construct, and the unique check is one call per unique property.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | handoff | - | - | planned from the C8-tc split | K0 G2 G3 D1 |
