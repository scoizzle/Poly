# TASK C8-tc - Typed create in the compiled tree
Status: planned. Branch slice/c8-tc-typed-create. Lane B. Review 2. Implement mill: Grok (review on OpenCode).
## Scope
Findings K0, G1, G2, G3+D1, G4+D15, G6, G8, G9, G12. Lowering names string-named store jobs and the exporter backs them with a dictionary. `LowerRuntimeFactoryCall` (`Poly/DomainModeling/Lowering/EffectLoweringPass.cs:1136-1198`), reached by `CreateEntityInstance` `:897`, `CreateEntityInRelationship` `:909`, `LowerCreateInProbe` `:1104` and `LowerCreateEntityInstanceProbe` `:1122`, emits `this.Create(name, prop, value, …)` / `this.CreateIn` / `this.ProbeCreate` with an object slot cast (`:1160-1162`); `WrapConstrainedAssign` `:312` emits `this.EnsureUnique(name, value)`. `DomainToCSharpExporter.StoreBind.cs` (318 lines) backs them with `Dictionary<string,object>` overloads (`:58-95`), `BindCreate`/`BindCreateIn`/`BindProbeCreate` (`:97-191`), `ValueFromDictionary` (`:279-299`) and an always-Success `EnsureUnique` stub (`:25-35`); goldens hold 1296 `new Dictionary<string, object` in 48 files under `Poly.Tests/DomainModeling/Compile/EmitGolden/`. The typed static `Entity.Create(…)` (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:838`) and the `Create{Nav}(…)` factory (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs:92`, also `AddCreateSingularNavMethod` `:84`) already exist. The interpreter routes `Create`/`CreateIn`/`ProbeCreate` in `DomainEntityInstance.InvokeNamed` (`Poly/DomainModeling/Runtime/DomainEntityInstance.InvokeNamed.cs:26-27`) to `RuntimeCreateFactory` (`Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:410-443`) reading name/value pairs; `EnsureUnique` is `HostAbi.cs:36-42`. Folds C6b2: the self-referencing collection slot currently passes `this` where the ctor wants `IEnumerable<T>` (`BuildTargetCreateArgs` tests `IsBackReference` `:225` before `IsCollection` `:232`).
K0 `orders/Customer.cs.golden:111`: keep dictionaries in the interpreter; print typed `Type.Create` / `Create{Nav}` only. G1 `university/Student.cs.golden:69`: `create in` becomes `this.CreateEnrollments(offering)` (also Guest.BookStay, Warehouse.RegisterTruck, Account.PlaceHq, Patron.CheckOut, Opportunity.Log/AddLine). G2 `university/Enrollment.cs.golden:228`: `BindProbeCreate` must not call `Enrollment.Create` (it `AttachEnrollments`s; probe-then-create duplicates children). G3+D1 `orders/Customer.cs.golden:110`: `EnsureUnique` is a real unique check against the identity set, not `=> DomainResult.Success()`. G4 `mcp-library/Patron.cs.golden:86` + D15 `:84`: `when loans Overdue` emits `this.CreateFines(...)` (Fine is added to `Patron.Fines`), failure is `DomainResult`, no `ConstraintFailureException` throw, drop dead `created1`. G6 `crm/Account.cs.golden:59`: `CreateParent` passes null for the new account's parent and only sets `this.Parent`. G8 `orders/OrderItem.cs.golden:26`: after setting the singular nav, attach `this` onto the new parent's inverse collection. G9 `orders/Customer.cs.golden:26`: `Create{Nav}` is public. G12 `orders/Poly.Types.cs.golden:39`: `DomainResult<T>.Failure` must not expose `default!` as `.Value` (`T?` or throw when `!IsSuccess`).
## Files
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (typed create / create-in / probe; typed `EnsureUnique`; G1, G4, D15)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.StoreBind.cs` (delete; K0, G2, G3, D1)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` (drop the `AddStoreBindMethods` call `:866`; static `Create` `:838`)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` (`AddCreateNavMethod` `:92`, `BuildCreateUniqueChecks` `:484`; collection slot before back-ref for C6b2; G6 `:129`, G8 `AddCreateNavMethod`, G9 `:271`)
- `Poly/DomainModeling/Lowering/DomainProgramProjection.cs` / `DomainToCSharpExporter.Actions.cs` (G12 `DomainResult<T>.Failure`)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.InvokeNamed.cs` (`:22-27`)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` (`RuntimeCreateFactory` `:410-443`, `Create`/`CreateIn`/`ProbeCreate` `:472-496`, `EnsureUnique` `:36`)
- `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` (real `EnsureUnique` `:62`, `Create`/`CreateIn`/`ProbeCreate` `:78-122`)
- `Poly.Tests/DomainModeling/Lowering/StoreBindCreateTests.cs`, `Poly.Tests/DomainModeling/Lowering/StoreBindUniqueTests.cs`, `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs`, `Poly.Tests/DomainModeling/Lowering/ParityTests.cs`, `Poly.Tests/DomainModeling/Lowering/Item4FailBeforeMutateTests.cs`, `Poly.Tests/DomainModeling/Runtime/DomainInstanceHostJobsTests.cs`, `Poly.Tests/DomainModeling/Compile/EmitGoldenTests.cs`, `Poly.Tests/DomainModeling/Compile/EveryConceptHasTreeTests.cs`
- Goldens: regenerate `Poly.Tests/DomainModeling/Compile/EmitGolden/**` (48 files change; none hand-edited)
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `create Fine { Amount: 5 }` body (K0) | `var create = this.Create("Fine", "Amount", 5L)` (`DomainResult<object>`) then `create.Value` unwrap | typed `Fine.Create(5L, …defaults)` returning `DomainResult<Fine>`, no cast to object | `StoreBindCreateTests`; `EmitGoldenTests.Emit_MatchesGolden` (`simulate-create-type/Fine.cs.golden`); K0 `orders/Customer.cs.golden:111` |
| `create in patron { Name: "x" }` body (G1) | `this.CreateIn("patron", "Name", "x")` / `this.ProbeCreate("Enrollment", "section", (object)offering)` | `this.CreatePatron("x")` / `this.CreateEnrollments(offering)` | `StoreBindCreateTests`; G1 `university/Student.cs.golden:69`; golden `simulate-create-in/*` |
| Fail-before-mutate probe (G2) | `this.ProbeCreate(...)` → `BindProbeCreate` calls `Enrollment.Create` (attaches) | typed constraint probe, no construct, no attach | `Item4FailBeforeMutateTests`; G2 `university/Enrollment.cs.golden:228` |
| Unique assign / unique on create (G3+D1) | `this.EnsureUnique` stub returns `DomainResult.Success()` always | real typed check (`DomainInstanceStore.EnsureUnique` `:62`) | `StoreBindUniqueTests`; G3+D1 `orders/Customer.cs.golden:110` |
| `when loans Overdue` create Fine (G4+D15) | `this.Create("Fine", …)`, throw `ConstraintFailureException`, discard `created1`; Fine never on `Patron.Fines` | `this.CreateFines(...)`; failure is `DomainResult`; no dead local | G4 `mcp-library/Patron.cs.golden:86`; D15 `:84` |
| Self-rel `CreateParent` (G6) | `Account.Create(..., this, …)` then `this.Parent = account` (cycle) | new account's parent is null; only `this.Parent` is set | G6 `crm/Account.cs.golden:59` |
| Singular `CreateOrder` (G8) | sets `this.Order`, skips `order.AttachItems(this)` | after setting the singular nav, attach `this` onto the inverse | G8 `orders/OrderItem.cs.golden:26` |
| `Create{Nav}` accessibility (G9) | typed `CreateOrders`/`CreateItems`/`CreateCustomer` are `private` | `Create{Nav}` is public | G9 `orders/Customer.cs.golden:26` |
| `DomainResult<T>.Failure` (G12) | `Value` is `default!`; callers `(Loan)create1.Value` | `T? Value` or throw on `.Value` when `!IsSuccess` | G12 `orders/Poly.Types.cs.golden:39` |
| Self-rel collection slot (`Node.children: many Node`) | `Node.Create(name, this, this)` — CS1503 (C6b2) | `Node.Create(name, this, new List<Node>())` | `DomainToCSharpExporterTests` (export compiles) |
| Any golden | 1296 `new Dictionary<string, object` in 48 files; `this.Create(`/`CreateIn(`/`ProbeCreate(` string-named | zero dictionary constructions; zero string-named create calls | new golden scan / `git grep` in the PR |
## Done when
- `EffectLoweringPass` emits typed factory calls: `Entity.Create(realParams, defaults)` for create, `this.Create{Nav}(…)` for create-in, and a typed probe that does not construct or attach; result stays `DomainResult<T>` with no cast to object.
- `StoreBind.cs` is deleted and `AddStoreBindMethods` is gone; the generated `EnsureUnique` is a real unique check (G3+D1), not an unconditional Success stub.
- The interpreter maps typed factory calls onto its dictionary-backed `DomainEntityInstance` via `HostAbi` / `InvokeNamed`; simulation and print agree.
- G4+D15: overdue Fine uses `CreateFines` and `DomainResult`; G6: `CreateParent` is acyclic; G8: singular inverse attaches; G9: `Create{Nav}` is public; G12: failed `.Value` is not `default!`.
- Folds C6b2: a self-referencing collection slot gets `new List<T>()` (not `this`) in every emit site.
- No golden contains `new Dictionary<string, object` or a string-named `Create` / `CreateIn` / `ProbeCreate` / `EnsureUnique`; each cited finding golden no longer shows that finding; goldens are regenerated, not hand-edited. Full suite green.
## SHIP if / NOT SHIP if
SHIP if the two golden greps (`new Dictionary<string, object`, string-named create calls) are empty, every finding id in this slice is gone from its cited golden, typed calls carry real parameters and defaults, and the suite passes with regenerated goldens.

NOT SHIP if any string-named create survives, if the compiled tree still constructs `Dictionary<string, object>`, if probe still mutates, if `EnsureUnique` is still a Success stub, if any listed finding remains, if a golden was hand-edited, or if a self-referencing collection slot still passes `this`.
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindCreateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindUniqueTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Item4FailBeforeMutateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainInstanceHostJobsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EveryConceptHasTreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
Lowering swaps one string-named call for the typed factory (and the object cast drops), `StoreBind.cs` is deleted whole, probe stops calling `Create`, unique becomes a real check, and G6/G8/G9/G12 are local emit edits; Scot can open those spots cold.
## Needs Scot
none
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | opencode/opencode-go/deepseek-v4.1-flash | 931b1461 | planned | - |
| 2026-10-10 | planner | grok/grok-4.6 | c0bd182b | re-planned: grew to K0 G1 G2 G3+D1 G4+D15 G6 G8 G9 G12 | - |
