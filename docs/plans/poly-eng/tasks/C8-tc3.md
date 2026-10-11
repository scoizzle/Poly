# TASK C8-tc3 - Typed create in the compiled tree (3 of 3: side-effect-free probe, void-context create, nav fixes)
Status: planned. Branch slice/c8-tc3-create-semantics. Lane B. Review 2. Implement mill: Grok (review on OpenCode).
## Scope
Split of C8-tc (Scot 2026-10-10 19:58 CT). Findings G2, G4+D15, G6, G8. Depends on C8-tc2; C8-lc, C8-web, C8-num, C8-clean and C8a2 follow this slice.
G2: the printed probe must not construct or attach. The static `Entity.ProbeCreate` that C8-tc1 emitted as a `Create`-delegating bridge becomes constraints-and-unique only: run `BuildCreateConstraintChecks` and the per-property unique index check, return `DomainResult.Success()`/`Failure`, never call `Create` (today's `BindProbeCreate` calls `Enrollment.Create`, which `section.AttachEnrollments(created)`s — `university/Enrollment.cs.golden:255`; probe-then-create duplicates children). Lowering already emits `Entity.ProbeCreate(args)` (C8-tc1); the interpreter probe job stays the dictionary `ProbeCreate` (already side-effect-free: "Probe does not register a child", `DomainEntityInstance.HostAbi.cs:428`).
G4+D15: a create inside a void/subscription context attaches to the unique collection instead of being a free `this.Create("Fine", …)` job. `CreateEntityInstance` (`Poly/DomainModeling/Lowering/EffectLoweringPass.cs:897`) emits `this.CreateFines(amount, reason)` when the context has no `ActionResultType` and the entity has exactly one collection nav of that type; the subscription handler becomes `internal DomainResult WhenEachLoanOverdue(Loan loan)` returning `DomainResult.Success()`/`Failure` (exporter subscription emission `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:566-691` region; pass `ActionResultType: DomainResult` into the subscription `LoweringContext`), failure returns instead of throwing `ConstraintFailureException`, and the dead `created1` local is gone (`mcp-library/Patron.cs.golden:84-86`). The interpreter's subscription executor returns the failed `DomainResult` instead of throwing (`DomainEntityInstance.HostAbi.cs:392-393` `ThrowIfEffectListFailed(ExecuteCachedSubscriptionTree(…))`, master `60089ef0` — re-check after C8-tc1/tc2).
G6: `Create{Nav}` for a self-rel parent passes null, not `this`, when the factory *is* `Create{parent}` (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs:129-135`: null when `parameter.Name == rel.Name`; `crm/Account.cs.golden:59` `Account.Create(…, this, …)` becomes null and only `this.Parent = account` is set).
G8: after assigning a singular nav, attach `this` onto the new parent's inverse collection (`AddCreateNavMethod` body, `Notify.cs:239-242` region: `local.Attach{Inverse}(this)`; `orders/OrderItem.cs.golden:26` `CreateOrder` sets `this.Order` but never `order.AttachItems(this)`).
## Files
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` (ProbeCreate body: constraints + unique only; subscription `LoweringContext` gets `ActionResultType: DomainResult`; handler emission returns `DomainResult`)
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (`CreateEntityInstance` void-context unique-collection call)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` (G6 self-rel slot; G8 inverse attach)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` (subscription failure returns `DomainResult`, no throw)
- `Poly.Tests/DomainModeling/Lowering/Item4FailBeforeMutateTests.cs`, `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs` (probe no-construct; CreateFines; CreateParent null; inverse attach), `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` (overdue-fine and probe parity rows), `Poly.Tests/DomainModeling/Compile/EmitGoldenTests.cs`
- Goldens: regenerate `Poly.Tests/DomainModeling/Compile/EmitGolden/**` (`university/Enrollment.cs.golden`, `mcp-library/Patron.cs.golden`, `crm/Account.cs.golden`, `orders/OrderItem.cs.golden` and the other probe/handler goldens; none hand-edited)
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Probe of a create (G2) | `BindProbeCreate`/bridge calls `Enrollment.Create` → `section.AttachEnrollments(created)` (`university/Enrollment.cs.golden:255`); probe-then-create duplicates children | `Enrollment.ProbeCreate(args)` checks constraints + unique only; no `new`, no `Attach*` | `Item4FailBeforeMutateTests`; new no-attach row in `DomainToCSharpExporterTests`; `university/Student.cs.golden:69` probe is `Enrollment.ProbeCreate(…)` |
| `when loans Overdue` create Fine (G4+D15) | `this.Create("Fine", "Amount", …)`, throw `ConstraintFailureException`, dead `created1` (`mcp-library/Patron.cs.golden:84-86`) | `this.CreateFines(loan.Amount, "Overdue")`; handler returns `DomainResult`; Fine lands on `Patron.Fines` | `ParityTests` overdue row; `DomainToCSharpExporterTests` |
| Self-rel `CreateParent` (G6) | `Account.Create(…, this, …)` then `this.Parent = account` (cycle) | new account's parent is null; only `this.Parent` is set | `DomainToCSharpExporterTests`; `crm/Account.cs.golden:59` |
| Singular `CreateOrder` (G8) | sets `this.Order`, never attaches | `order.AttachItems(this)` after the assignment | `DomainToCSharpExporterTests`; `orders/OrderItem.cs.golden:26` |
## Done when
- Probe never constructs or attaches: the exported `ProbeCreate` body has constraint and unique checks only, the cited Enrollment golden no longer attaches from probe, and Item-4 ordering tests stay green.
- G4+D15: the overdue Fine handler uses `CreateFines`, returns `DomainResult`, does not throw, and has no dead local; the interpreter subscription failure path returns instead of throwing.
- G6: `CreateParent` passes null for the new account's parent; G8: singular nav assign attaches the inverse collection.
- Each cited golden no longer shows its finding; full suite green; no golden hand-edited.
## SHIP if / NOT SHIP if
SHIP if the four cited goldens match the After column, probe has no side effects on either side, and the suite passes.

NOT SHIP if probe still constructs or attaches, the overdue handler still throws or discards a result, `CreateParent` still cycles, the singular inverse still fails to attach, or any listed finding is deferred.
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Item4FailBeforeMutateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
The probe body drops its one `Create` call, the handler swaps throw for return, the self-rel slot swaps `this` for a typed null, and the inverse attach is one `Invoke`; Scot opens those four spots cold.
## Needs Scot
none
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | opencode/opencode-go/mimo-v2.6-flash | 60089ef0 | planned | G2 G4+D15 G6 G8; split of C8-tc per Scot 19:58 CT |
