# TASK C2b - Run the compiled Create checks; delete the C# twins
Status: in review. Branch slice/c2b-create-initializers-followup. Lane B. Review 2. Implement mill: Grok (review on OpenCode).

## Scope
C2a (#134 `ede905f9`) already sends HostAbi `Create` / `CreateIn` / `ProbeCreate` (`Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:555-578`) through `StoreOrDefault` (`:585-591`). The card's `HostAbi.cs:658, 729` callers are gone (file is 645 lines). Remaining C# twins: `FillCreateDefaults` (`DomainEntityInstance.cs:152-167`) and `ValidateCreateConstraints` (`:360-365`, wraps `ValidateConstraints` `:176-232`, C4a `EqualityConstraint` arm `:212-220`) at `DomainInstanceStore.ProbeCreate` `:149-150` and `CreateCore` `:174-175`. `DomainEntityInstance.Create` `:135-137` also calls `ValidateConstraints` (no store, so no unique).
The compiled factory already holds the checks (`BuildCreateConstraintChecks` `DomainToCSharpExporter.Notify.cs:331-456`, static `Create` at `DomainToCSharpExporter.cs:820-827`, `IsStatic: true`, added before store-job `Create` overloads at `:848`). Printed `BindCreate` / `BindProbeCreate` already invoke it (`StoreBind.cs:117-120, 151-153`). Domain-bound simulate must run that factory's leading Failure-return `IfStatement`s via `GetOrLower` + `TryGetModuleMethod` (pick `IsStatic: true`). Stop before `created = new` — that tail constructs a printed instance, not a `DomainEntityInstance`. SetArgs slot 0 is unused (checks read `Parameter`s, never `this`); slots 1+ are factory parameters from the values dict (`ToCamelCase` names), missing slots get `Parameter.DefaultValue`.
Delete both helpers and the equality arm. `CreateCore` calls `DomainEntityInstance.Create` then `TryAdd` (`:192-195`). Factory Failure throws `ConstraintFailureException`; `CreateCore` `:184-185` must catch it next to `InvalidOperationException`. `ProbeCreate` runs the same prefix and returns Failure without allocating. Unique-on-create stays `TryAdd` (C4b adds it to the factory; printed `BindProbeCreate` already skips unique). Domain-null `Create` keeps required/range/length/pattern in `ValidateConstraints` (no equality) so `Create_EnforcesConstraints_FailLoud` still throws `InvalidOperationException`. No new type.

## Files
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` (run factory-check prefix from `Create`; delete `FillCreateDefaults`, `ValidateCreateConstraints`, C4a equality arm)
- `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` (drop `:149-150` and `:174-175`; catch `ConstraintFailureException` in `CreateCore`)
- `Poly.Tests/TestHelpers/ParitySides.cs` (`SimulateSide.CreateCore` `:13-21`: map `ConstraintFailureException` to `(false, message, null)`)
- `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` (flip C2b KnownGaps; add required/length/pattern/defaults rows; `Create_WhenEqualityViolated_FailsWithTheSameMessage` becomes `AssertAgree`)
- `Poly.Tests/DomainModeling/DomainEntityInstanceTests.cs` (`Create_EnforcesConstraints_FailLoud` `:3591` stays IOE on Domain-null)
- `Poly.Tests/DomainModeling/Lowering/StoreBindCreateTests.cs`
- `Poly.Tests/DomainModeling/Lowering/Item4FailBeforeMutateTests.cs`
- `Poly.Tests/DomainModeling/Runtime/DomainInstanceHostJobsTests.cs`

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Domain-bound root `Create` out of range (`Score: 99` on `range(1, 10)`) | Simulate throws IOE from `ValidateConstraints` `:135-137`; printed factory returns Failure | Both Failure, same `'Score' must be <= 10.` message; no exception-type split | flip `ParityTests.KnownGap_CreateOutOfRange_SimulateThrowsAndPrintedReturnsFailure` to `AssertAgree` |
| Equality on create (API-built `EqualityConstraint`) | Messages already agree; reporting is throw vs Failure (`CreateThrowsVersusFails` `:338`) | Both Failure, same `'Status' must equal Active.` / `'Level' must equal 5.` | `Create_WhenEqualityViolated_FailsWithTheSameMessage` becomes `AssertAgree`; `Create_WhenEqualityHolds_Agrees` stays |
| Equality on an entry-assigned property (`Mark` set in first-stage entry) | Factory skips entry-assigned (`Notify.cs:340-342`); simulate rejects before entry (`KnownGap_EqualityOnEntryAssignedProperty_SimulateRejectsCreate` `:492`) | Simulate skips it too; both succeed | flip that KnownGap to `AssertAgree` |
| Required / length / pattern / defaults on create | C# twins; no parity row per kind | Factory-check prefix; each kind has an `AssertAgree` row | new `ParityTests` rows (required omit, `length` too-short, `pattern` miss, omitted `default(5)` lands) |
| Store `ProbeCreate` / `Create` (action `create Type`) | `FillCreateDefaults` + `ValidateCreateConstraints` then `DomainEntityInstance.Create` | Prefix only; then `Create` + `TryAdd`; Item 4 still fails before `OpenStays` mutates | `Item4FailBeforeMutateTests.StoreLess_OccupyIfOnMutatedProperty_ProbeCreateFailsBeforeOpenStaysAssign`; existing `StoreBindCreateTests` |
| Unique on create | Store unique inside `ValidateCreateConstraints` and again at `TryAdd` `:192-195` | `TryAdd` only (C4b puts unique in the factory) | `ActionEntityReturnTests.InvokeAction_CreateInUniqueCollision_DoesNotApplyPriorAssigns` |
| Domain-null `Create` (no module) | `ValidateConstraints` throws IOE | unchanged except equality arm gone | `DomainEntityInstanceTests.Create_EnforcesConstraints_FailLoud` |
| First-stage entry transition | Simulate skips transition effects (`ApplyInitialStageEntryEffects` `:253-267`) | unchanged (check prefix only; do not run the ctor tail) | `ParityTests.KnownGap_FirstStageEntryTransition_SimulateStaysInFirstStage` stays red-on-purpose |

## Done when
- `git grep ValidateCreateConstraints -- Poly Poly.Tests` and `git grep FillCreateDefaults -- Poly Poly.Tests` are empty.
- C4a equality arm in `ValidateConstraints` is gone (comment at `:212-213` already names this slice).
- Parity `AssertAgree` rows exist for required, range, length, pattern, equality, and defaults (create, both modes). `KnownGap_CreateOutOfRange_*` and `KnownGap_EqualityOnEntryAssignedProperty_*` are flipped. `KnownGap_FirstStageEntryTransition_*` is not flipped.
- Domain-null `Create_EnforcesConstraints_FailLoud` still throws `InvalidOperationException`. Unique-on-create still fails via `TryAdd`. Full suite green.

## SHIP if / NOT SHIP if
SHIP if both greps are empty and every check-kind parity row `AssertAgree`s (red on master for the KnownGaps, green after).

NOT SHIP if any check kind is covered only by suite-green, if simulate still throws on root Create while print returns Failure, if the factory `new Entity` tail runs on a `DomainEntityInstance`, if unique-on-create is moved into the factory (C4b), or if `KnownGap_FirstStageEntryTransition_*` is quietly flipped.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindCreateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Item4FailBeforeMutateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainInstanceHostJobsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ActionEntityReturnTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/OnEntryConstraintAgreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
The store drops two helper calls, Create runs the factory's existing If-Failure prefix, and the constraint switch loses its equality arm; Scot can open those three spots cold.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-09 | planner | grok/grok-4.6 | `ede905f9` | planned | - |
| 2026-10-09 | implementer | grok/grok-4.6 | `4b870fce` | pushed | tests 3467/3467; sweep: factory prefix runs; twins gone |
