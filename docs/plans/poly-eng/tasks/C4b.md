# TASK C4b - Unique on create in the factory tree
Status: merged #139 `b894d9a6` 2026-10-09. Branch slice/c4b. Lane B. Review 1. Implement mill: Grok (review on OpenCode).
## Scope
C2b (#136 `10e6570a`) runs the static `Create` Failure-return Ifs (`BuildCreateConstraintChecks` `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs:331-456`) and stops before `created = new` (`CreateFactoryCheckPrefix` `DomainEntityInstance.cs:224-235`). Unique on create is still the store scan: `TryAdd` (`DomainInstanceStore.cs:53-61`, `CreateCore` `:188-191`) with message `UniqueCollisionForProperty` `:323-345`. The factory switch skips `UniqueConstraint` (`Notify.cs:448-451`; card said `:448-450`). Assign already wraps `this.EnsureUnique` then Failure (`EffectLoweringPass.WrapConstrainedAssign` `:305-314`). Printed `EnsureUnique` is a Success stub (`StoreBind.cs:25-35`). `Create` is `IsStatic: true` (`DomainToCSharpExporter.cs:820-827`), so the call goes on `created` after `new` (`:774-778`), before Attach. Do not put `this` in the prefix (would not compile). Leave the stub and `TryAdd`. No new type.
## Files
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` (`BuildCreateUniqueChecks`; drop the skip comment at `:448-451`)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` (splice those nodes after `created = new` `:774-778`; add `uniqueCheck` to the `Create` Block locals `:824`)
- `Poly.Tests/DomainModeling/Lowering/StoreBindUniqueTests.cs` (ExtractMethod of static `Create` contains `EnsureUnique`)
- `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` (duplicate-create row)
- `Poly.Tests/DomainModeling/Compile/EmitGoldenTests.cs` + goldens (printed `Create` bodies change; call out in the PR)
- Named and left alone: `StoreBind.cs` stub, `TryAdd`, `CreateFactoryCheckPrefix`, `HostAbi.EnsureUnique` `:36-41`, `BindProbeCreate`
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Unique property on static `Create` (`Plate: Text unique`) | Factory skip `:448-451`; `ExtractMethod(..., "static DomainResult<Permit> Create(")` has no `EnsureUnique` invoke (the stub method still exists) | After `created = new`: `uniqueCheck = created.EnsureUnique("Plate", plate); if (!uniqueCheck.IsSuccess) return Failure(ErrorMessage ?? "")` | new `StoreBindUniqueTests.Export_CreateFactory_InvokesEnsureUnique` (red on master) |
| Duplicate root create (two `Create`s, same unique `Code`) | Simulate: first ok, second `Add` throws IOE `Unique constraint violated: 'Code' value is already used on another 'Widget'.` Print: both succeed (stub, no store) | Same split: unique after `new` is not in `CreateFactoryCheckPrefix`; stub still Success; `TryAdd` still the simulate gate | `ParityTests.KnownGap_CreateDuplicateUnique_SimulateThrowsAndPrintedSucceeds` (pin `Differences` from a master run) |
| Assign unique | `this.EnsureUnique` then assign | unchanged | `StoreBindUniqueTests.UniqueAssign_Export_LowersToEnsureUniqueThenAssign`; `UniqueAssign_WithStore_Collision_IsFailureWithoutMutating` |
| Store `Create` / `CreateIn` unique collision | `TryAdd` Failure, no register | unchanged (`TryAdd` stays) | `StoreBindCreateTests.Store_CreateIn_UniqueCollision_IsFailureWithoutRegistering`; `ActionEntityReturnTests.InvokeAction_CreateInUniqueCollision_DoesNotApplyPriorAssigns` |
| `ProbeCreate` unique | Simulate prefix has no unique; printed `BindProbeCreate` calls `Target.Create` which also skipped it | Simulate prefix still skips (stops at `new`); printed `Create` now calls stub `EnsureUnique` (still Success) | `Item4FailBeforeMutateTests`; leave `BindProbeCreate` |
| Goldens with a unique column (mcp-library `Book.Isbn`, Patron `Email`) | `Create` has range/required Ifs only | `Create` body calls `EnsureUnique` | `EmitGoldenTests.Emit_MatchesGolden` (regenerate; PR names the files) |
| `UniqueConstraint` in the H4 ratchet | method `EnsureUnique` exists (`EveryConceptHasTreeTests.cs:219-220`) | still true (now also an invoke in `Create`) | `EveryConceptHasTreeTests` |
## Done when
- Static `Create` invokes `EnsureUnique` for each non-entry-assigned `UniqueConstraint` property, after `created = new`, before Attach / `Success`. Skip comment at `Notify.cs:448-451` is gone.
- Printed `EnsureUnique` stays `return DomainResult.Success()` (`StoreBind.cs:32-34`). `TryAdd` still enforces unique on simulate. `CreateFactoryCheckPrefix` still stops at `Assignment { Value: New }`.
- `Export_CreateFactory_InvokesEnsureUnique` is red on master (no invoke in the `Create` body) and green after. Duplicate-create `KnownGap` pins the simulate-IOE / print-success split. Goldens match. Full suite green. PR calls out the printed `Create` change.
## SHIP if / NOT SHIP if
SHIP if `Export_CreateFactory_InvokesEnsureUnique` is red on master and green after, goldens show the invoke, and the PR calls out the printed change.

NOT SHIP if the printed change is not called out, if static `Create` uses `this` (does not compile), if the Success stub is replaced with a registry or scan, if `TryAdd` unique is deleted, or if `CreateFactoryCheckPrefix` walks past `new`.
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindUniqueTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindCreateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ActionEntityReturnTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Item4FailBeforeMutateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EveryConceptHasTreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
One `EnsureUnique` / `if (!IsSuccess) return Failure` block after `created = new`, same shape as assign; Scot can open the factory body cold.
## Needs Scot
none
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-09 | planner | grok/grok-4.6 | `10e6570a` | planned | - |
| 2026-10-09 | implementer | grok/grok-4.6 | `cd9fb4e5` | pushed | tests 3479/3479; sweep: created.EnsureUnique after new; prefix still stops at new |
| 2026-10-09 | reviewer | opencode/opencode-go/deepseek-v4.1-flash | `ee25ae5a` | NOT SHIP | https://github.com/scoizzle/Poly/pull/139#issuecomment-6089962049 (Grug NOT SHIP at ee25ae5a, 5 open findings, mode full) |
| 2026-10-09 | implementer | grok/grok-4.6 | `dc26467b` | fixes pushed | R1, R2, R3, R4; disputed: none; R5 skipped (nit >1 line); tests 3479/3479 |
| 2026-10-09 | planner | opencode-go/deepseek-v4.1-flash | `b894d9a6` | merged #139 | squash-merged to master |
