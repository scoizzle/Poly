# ADR: Produced operations call the storage context, and the interpreter runs that body

**Date:** 2026-10-15. Revised 2026-10-16.
**Status:** Accepted direction (spike). No generator change.
**Deciders:** Spike on `docs/produced-code-di` at `ff83c598`, revised in the same session after the uniqueness follow-up.

**Related:** [`2026-09-05-lowered-module-is-domain-meaning.md`](2026-09-05-lowered-module-is-domain-meaning.md) · [`2026-09-04-frozen-core-pipeline.md`](2026-09-04-frozen-core-pipeline.md) · [`2026-09-03-facts-concerns-bags-store-bind.md`](2026-09-03-facts-concerns-bags-store-bind.md) · [`2026-08-15-domain-library-extensions-mcp-harness.md`](2026-08-15-domain-library-extensions-mcp-harness.md) · [`2026-07-22-persistence-units-medium-facets-pack-syntax-export.md`](2026-07-22-persistence-units-medium-facets-pack-syntax-export.md) · probe [`docs/probes/produced-di/`](../probes/produced-di/README.md)

## Decision

Produced domain code does not take `IServiceProvider`. The container stays in the host file, where it already constructs the scoped `DbContext`.

An operation that needs storage calls a static helper on the owning type. The parameter is the context the loaded storage subsystem already defines. For the EF packs that type is `{Domain}DbContext`. The helper exists because analysis already recorded which properties are unique. `Create` takes the same context, calls the helper before it constructs, and adds the instance so a later call in the same operation can see it. Required, length, and pattern stay inline and do not take the context.

The interpreter executes that produced method. It does not keep a second uniqueness scan, link table, or subscription walk beside it. When a persistence library is loaded, the simulate host constructs the same `DbContext` on Sqlite (`Data Source=:memory:`) and passes it into `Create`. EF Core is not a reference of core `Poly/`. The in-memory EF provider answers `Any()` and does not enforce `HasIndex(...).IsUnique()`, so it is not the stand-in.

`DomainInstanceStore` stops being the persistence runtime. Its instance list, unique scan, and relationship side table duplicate the context. Its subscription metadata walk duplicates `Notify{Stage}Subscribers`, which the module already lowers and the printer already emits. Stage and subscription behavior is that produced method. The interpreter runs it.

No new `uses` id. No new port interface in the domain file. The storage library writes the helper and the `Create` parameter from the bag it already publishes, and the host file passes the context the route already has.

## Target shape

`Lab` with `Widget.Code` unique. This is the domain in `ParityTests.KnownGap_CreateDuplicateUnique_SimulateThrowsAndPrintedSucceeds`. The compiler does not emit this yet.

```csharp
public static bool ExistsWithCode(LabDbContext db, string code) =>
    db.Widgets.Local.Any(w => w.Code == code)
    || db.Widgets.Any(w => w.Code == code);

public static DomainResult<Widget> Create(string code, LabDbContext db)
{
    if (ExistsWithCode(db, code))
    {
        return DomainResult<Widget>.Failure(
            "Unique constraint violated: 'Code' value is already used on another 'Widget'.");
    }
    var created = new Widget(code);
    db.Widgets.Add(created);
    return DomainResult<Widget>.Success(created);
}
```

The failure string is the one `DomainInstanceStore.UniqueCollisionForProperty` already returns (`DomainInstanceStore.cs:335`). The check sits before `new`, which is the prefix `RunCreateFactoryChecks` already executes (`DomainEntityInstance.cs:178-235`). `Local` is in the check so a sibling added earlier in this operation is visible before `SaveChanges`.

A nested create is the same helper on the child type. `Permit.ExistsWithPlate(db, plate)` is a static method because `Plate` is unique. The parent passes the context it was given. The parent sees `Failure` and does not link the child. That is the outcome `ActionEntityReturnTests.InvokeAction_CreateInUniqueCollision_DoesNotApplyPriorAssigns` records for plate `ABC123`.

Another storage subsystem supplies another parameter type and another body for the same method. The call site stays `Widget.ExistsWithCode(db, code)`. SQLite and SQL Server differ in the host fragment that builds the context (`UseSqlite` versus `UseSqlServer`, `MinimalApiGenerator.BuildProviderRegistration`, `MinimalApiGenerator.cs:109-122`), not in the call.

## Why the context has to be on Create

`Order.Number` unique still prints as a factory that never asks, and an `EnsureUnique` that returns success:

```csharp
public static DomainResult<Order> Create(string number)
{
    var created = new Order(number);
    return DomainResult<Order>.Success(created);
}
public DomainResult EnsureUnique(string propertyName, object @value) => DomainResult.Success();
```

That is the Shop baseline (`docs/probes/produced-di/baseline/Order.cs`). `BuildCreateConstraintChecks` (`DomainToCSharpExporter.Notify.cs:411-558`) covers required, range, length, pattern, equality, and enum membership. It has no `UniqueConstraint` arm. `StoreBindUniqueTests.Export_CreateFactory_InvokesEnsureUnique` asserts the factory body does not contain `EnsureUnique`. The printed stub is `DomainToCSharpExporter.EnsureUniqueAssignMethod`.

The create route already holds the context and does not pass it:

```csharp
app.MapPost("/api/orders", async (OrderDto dto, ShopDbContext db) =>
{
    var orderResult = Order.Create(dto.Number);
    // ...
    db.Orders.Add(orderResult.Value);
    await db.SaveChangesAsync();
});
```

`AppendCreateCallStatements` (`MinimalApiGenerator.cs:422-499`) builds `(dto, db)` and calls `Entity.Create` with the DTO fields only. `ShopDbContext.OnModelCreating` emits `HasIndex(x => x.Number).IsUnique()`. A duplicate fails inside `SaveChangesAsync`, after `Create` has returned `Success`. The route maps `DomainResult` failure to `Conflict`. The database exception is not that result.

A nested call is the case that forces the parameter onto `Create` itself. `Lot.CreatePermits` calls `Permit.Create` and returns `Failure` only when that result is not success (`AddCreateNavMethod`). The lowered action does the same around `this.CreatePermits` (`EffectLoweringPass.LowerCreateNavCall`, `EffectLoweringPass.cs:931`). The Shop host's `db` is not on that stack. A lookup done only in the route leaves the inner factory returning `Success`.

Assign-time uniqueness already lowers to `this.EnsureUnique(propertyName, value)` (`EffectLoweringPass.WrapConstrainedAssign`, `EffectLoweringPass.cs:291-319`). On the interpreter, `DomainEntityInstance.EnsureUnique` forwards to `Store` when `Store` is set, and `TryAdd` rejects a collision by walking `_instances`. `ParityTests.KnownGap_CreateDuplicateUnique_SimulateThrowsAndPrintedSucceeds` (`ParityTests.cs:899`) pins the split: the second `Widget` with `Code = "A"` fails in simulate with `Unique constraint violated: 'Code' value is already used on another 'Widget'.` and succeeds in the printed factory.

`PrintedSide.CreateCore` calls `ExportedCSharp.InvokeCreate` with the property values only. `InvokeCreate` (`ExportedCSharp.cs:52-74`) fills a parameter it does not recognize with `null`. A context parameter on today's harness is null, and the helper cannot see the first widget. `SimulateSide` already keeps one store and calls `Add` after `Create` (`ParitySides.cs:10-27`). The printed side passes that same kind of value into `Widget.Create`.

## The interpreter runs the produced body

The lowered method is the program. The printer emits it. The interpreter executes it. A second implementation of the same fact is the dual path `2026-09-04` says not to grow.

`DomainInstanceStore` is that second implementation for persistence. It keeps `_instances` and `_links`. `TryAdd` walks the list to imitate a unique index (`UniqueCollisionForProperty`, `DomainInstanceStore.cs:316-338`). `Link`, `Unlink`, and `GetLinkedTargets` imitate navigation fixup. Sqlite with `Data Source=:memory:` enforces the unique index the `DbContext` already emits, tracks the graph, and is discarded with the connection. The helper above runs against it unchanged. EF's in-memory provider tracks entities and answers `Any()`. It does not enforce the unique index. It is not the replacement.

Notification fan-out is already a produced method. `Reservation.NotifyInHouseSubscribers` walks `_roomStaysInHouseSubscribers` and calls `WhenEachReservationInHouse` (`EmitGolden/hotel/Reservation.cs.golden:267-278`). `AddSubscriberRegistrationNodes` (`DomainToCSharpExporter.Notify.cs:19-62`) emits the `ForEach` that fills those lists. `ExecuteNotifyStageSubscribers` already loads `Notify{Stage}Subscribers` from the module and runs that tree (`DomainEntityInstance.HostAbi.cs:302-323`). `NotifyTransition` (`DomainInstanceStore.cs:522-536`) and `SubscriberFieldsForRelationship` (`:544-598`) walk the catalog and rebuild those field names as strings. That walk is a second copy of the method the module already has. The interpreter runs `NotifyInHouseSubscribers`.

Core `Poly/` takes no external package (`docs/CORE.md` §1). `Microsoft.EntityFrameworkCore.Sqlite` is referenced by `Poly.Tests` and by the demo hosts (`Directory.Packages.props`). The simulate host, which already has that package when it compiles produced code, constructs the context. `DirectVmAbiEmitter.InvokeInstanceMethod` can call a method on a context it was given. It cannot execute the helper as LINQ inside the VM: `Any` is an extension, and that lookup only sees methods declared on the instance. The VM does not learn EF. The host runs the produced method against the context.

A domain with no persistence library has no context parameter and no list invented to stand in for one. `unique` is a fact that storage will have to enforce. The bag and the helper appear when that library is loaded.

## Catalog

`Widget.ExistsWithCode` names `LabDbContext`. Today the entity artifact may point only at `source-entity`:

```263:264:Poly/DomainModeling/Compile/DomainSession.cs
catalog.DeclareType(ScaffoldingType, mayPointAt: [SourceDomainType]);
catalog.DeclareType(EntityType, mayPointAt: [SourceEntityType]);
```

`LabDbContext` is a generated tree (`DbContextArtifactContributor.cs:27-28`). A generated tree may point at nothing (`DomainSession.cs:230`). Recording the dependency is allowing the entity artifact to point at that generated tree. The storage library writes the helper while the entity tree is produced, from `StorageMappingMetadata` (the property is unique, the set is `Widgets`). `IStorageSyntaxEmitter` was the hook for a pack to decorate the host tree, and nothing implements it. The vendor switch in `BuildProviderRegistration` is the host fragment that still belongs to the pack.

## The original question

The spike asked whether produced C# should depend on Microsoft.Extensions.DependencyInjection. It should not. The follow-up was that `Create` needs a context so a uniqueness check can happen. That context is the `DbContext` parameter above, not a service locator.

### Container, host only

`Program.cs` already calls `AddDbContext`, `CreateScope`, and `GetRequiredService` (`EmitGolden/crm-dslcompiler/Program.cs.golden:16-25`, `MinimalApiGenerator.GenerateCompilationUnit` at `MinimalApiGenerator.cs:288-326`). Those assemblies live in the ASP.NET Core shared framework (`Microsoft.AspNetCore.App`), not in `Microsoft.NETCore.App`, and not as a `PackageReference` on `Poly/Poly.csproj`. `Poly.Mcp` reaches the namespace through `Microsoft.Extensions.Hosting` (`Poly.Mcp.csproj:16`). That process is the harness.

`{Domain}DbContext` does not mention the container. It takes `DbContextOptions<T>`. The artifact set has no `.csproj`. The customer host project owns the manifest. `docs/probes/evolve-2026-10-13/library-host/Demo.csproj` is a hand-written `Microsoft.NET.Sdk.Web` project plus `Microsoft.EntityFrameworkCore.Sqlite`.

### Domain files stay free of the container

Entity files, adapters, and subscription methods do not name `IServiceProvider`, `IServiceCollection`, or `Microsoft.Extensions.DependencyInjection`. A file printed from the module does not contain those names. Dropping the oracle's implicit using (`DslCompilerCompileOracleTests.cs:131`) fails `Program.cs` and does not fail `Order.cs`, `Poly.Types.cs`, or the `DbContext`.

The Shop slice (`docs/probes/produced-di/slice/`) showed the other collaborator shape, once: `Order.Pay(ChargeRequest, IBillingCharge)` and a host that resolves the implementation. `dotnet run --project docs/probes/produced-di/slice/Probe.csproj` printed `resolved 2500 USD` and exited 0. `BillingAdapters.Charge` still failed closed. The slice is a hand edit. It is not this decision's storage helper, and it is not a lowered tree. An outbound port still waits for a domain fact that names it. Uniqueness is already a fact. It does not need a new interface or a new `uses` id.

### `IServiceProvider` as the parameter — rejected

Passing the container through `Create` keeps one signature by hiding a different lookup in each body. `GetRequiredService<T>()` is a static extension. The VM's instance lookup does not find it, so print would bind it and simulate would throw. `GetService(Type)` is a real method on the value, and `TypeOf` is already a Syntax node, so the interpreter can call it. The call returns the context. The method the nested `Create` needs is `ExistsWithCode` on that context. The host performs the one resolution it already performs, and passes the context in.

A vendor-specific lookup fragment inside `Create` is a different program per `uses` id. The 2026-09-03 duty of a surface is to select the implementation, not to rewrite the operation. The operation calls `Widget.ExistsWithCode`. The pack supplies the parameter type, the helper body, and the host fragment that constructs the context.

A stringly `EnsureUnique(string entity, string propertyName, object value)` was considered and rejected. The bag already lists `Code`. The produced method is `ExistsWithCode`. Dictionary jobs stay an interpreter detail. They are not the printed shape.

## What would make us revisit

1. An outbound port the domain names (a clock, a billing adapter). The host resolves the implementation and the action takes it as a parameter, as the Shop slice did. Grammar and the DSL guide change in that same change. Uniqueness is not that work.
2. A process-lifetime loop with no request to pass the context through. The host file gains `AddHostedService` and calls the module methods with the context. Still no container type in the module.
3. Two persistence units in one host. Keyed registration is host text. The trigger is a domain that has two units.
4. A produced type that needs a package outside `Microsoft.AspNetCore.App` and outside the EF package the host already references. The host project gains the `PackageReference`. The artifact set still has no `.csproj`.

## Kill criteria

| Condition | Result |
|-----------|--------|
| `Main` or a host escape in core | Not required. The host file is the existing `http` contributor. EF stays in the simulate host and the customer host. |
| Consumer-specific lowering flag | The helper is in the module both executions run. A print-only `ExistsWithCode` the interpreter does not run is that flag. |
| Container type in the lowered module | Rejected. The parameter is the context type. |
| EF Core inside `Poly/` or the VM | Rejected. The host constructs the context. The VM does not translate LINQ. |
| `DomainInstanceStore` kept as the persistence runtime beside the helper | Rejected. One body. |
| Motive is "hosts usually inject services" | Not this decision. The requirement is a nested `Create` that can fail a unique check. |

## Non-goals

- No generator change, no golden edit, no audit edit, and no `docs/plans/poly-eng/board.md` row in this spike.
- No new `ArtifactCatalog` member and no new product type.
- No implementation of the trace-and-succeed adapter stub.
- The Shop slice stays a hand edit of a billing parameter. It does not gain `ExistsWithCode`.
