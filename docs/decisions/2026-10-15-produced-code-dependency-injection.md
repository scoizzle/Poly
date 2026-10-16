# ADR: Produced C# keeps the container on the host

**Date:** 2026-10-15
**Status:** Accepted boundary (spike). No generator change.
**Deciders:** Spike on `docs/produced-code-di` at `ff83c598`.

**Related:** [`2026-09-05-lowered-module-is-domain-meaning.md`](2026-09-05-lowered-module-is-domain-meaning.md) · [`2026-09-04-frozen-core-pipeline.md`](2026-09-04-frozen-core-pipeline.md) · [`2026-08-15-domain-library-extensions-mcp-harness.md`](2026-08-15-domain-library-extensions-mcp-harness.md) · [`2026-07-22-persistence-units-medium-facets-pack-syntax-export.md`](2026-07-22-persistence-units-medium-facets-pack-syntax-export.md) · probe [`docs/probes/produced-di/`](../probes/produced-di/README.md)

## Decision

The produced domain surface and the lowered operation module do not depend on Microsoft.Extensions.DependencyInjection. The emitted host already does, and that is the only produced seam.

The container lives in the `http` door's host artifacts (`Program.cs`, and `demo.http` only as a client script). It composes collaborators the host process owns: today a scoped `DbContext`, and the minimal-API handler parameters that receive it. Entity types, contract adapters, and subscription handlers take their collaborators as parameters or call a static fail-closed adapter. They do not name `IServiceProvider`, `IServiceCollection`, or the DependencyInjection namespace.

A clock, a logger, an HTTP client, an options bag, a keyed persistence unit, or a background dispatcher is the same seam: the host registers it and passes the instance into a module operation. Generating that registration, or teaching the DSL to name the port, waits until a domain fact or a second host-lifetime service is a real product requirement. Emitting `Add{Domain}()` with no new collaborator, or letting domain code call `GetRequiredService`, is not that requirement.

## The four seams

These are different questions. Only the host seam is a yes.

### 1. Poly's own projects — no MEDI package on the core library

| Project | Reference |
|---------|-----------|
| `Poly/Poly.csproj` | No `PackageReference`. `Microsoft.NET.Sdk` class library. |
| `src/Poly.DslCompiler/Poly.DslCompiler.csproj` | Project references only. |
| `src/Poly.Packs.*` | No DependencyInjection package. |
| `Poly.Tests/Poly.Tests.csproj` | TUnit, CodeAnalysis, `Microsoft.EntityFrameworkCore.Sqlite`, SQLitePCL. No DependencyInjection package. |
| `Poly.Mcp/Poly.Mcp.csproj:16` | `Microsoft.Extensions.Hosting` (pinned at `Directory.Packages.props:18`, version 10.0.9). |
| `Poly.Demo` | ASP.NET Core component packages and `Microsoft.Extensions.Logging.Debug`. The demo app, not produced output. |

`Directory.Packages.props` has no version for `Microsoft.Extensions.DependencyInjection` itself. The MCP harness imports the namespace and calls its extension methods (`Poly.Mcp/Program.cs:1`, registrations at lines 16–19). `AddSingleton` binds to `ServiceCollectionServiceExtensions` in assembly `Microsoft.Extensions.DependencyInjection.Abstractions`. That process is the harness. It is not a reference the produced domain library takes.

A search for a source declaration of `IServiceProvider` or `IServiceCollection` in the solution finds none. The generator mentions `AddDbContext` and `CreateScope` as Syntax member names (`src/Poly.DslCompiler/MinimalApiGenerator.cs:293`, `:325`), so the Poly compilation never binds those container types.

### 2. Emitted host artifacts — already yes, via the shared framework

`Program.cs` is the container client. The crm host golden is the shipped shape:

```16:25:Poly.Tests/DomainModeling/Compile/EmitGolden/crm-dslcompiler/Program.cs.golden
builder.Services.AddDbContext<CrmDbContext>(options => options.UseSqlite("Data Source=crm.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedAsync(db);
}
```

The same three calls are what `MinimalApiGenerator.GenerateCompilationUnit` emits (`src/Poly.DslCompiler/MinimalApiGenerator.cs:288-326`). `BuildProviderRegistration` (`:109-122`) picks `UseSqlite`, `UseSqlServer`, or `UseInMemoryDatabase`. Action routes already take the context as a handler parameter the host resolves, not as an argument of the domain method (`AppendActionEndpointStatements` adds `db` at `:665` for a nested route and `:702` for a root route; the golden capture route is `:912` and calls `entity.Capture(dto.request)` at `:933`).

`{Domain}DbContext.cs` does not mention the container. `CrmDbContext` takes `DbContextOptions<CrmDbContext>` (`CrmDbContext.cs.golden:7-9`). `demo.http` is request text. The Shop probe baseline matches this split: container calls exist only in `baseline/Program.cs`.

Those types are assemblies in the ASP.NET Core shared framework, not a NuGet package the artifacts declare. On this machine they are:

- `/usr/local/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.12/Microsoft.Extensions.DependencyInjection.dll`
- `.../Microsoft.Extensions.DependencyInjection.Abstractions.dll`

They are absent from `Microsoft.NETCore.App`. A `Microsoft.NET.Sdk.Web` project gets them from the implicit `Microsoft.AspNetCore.App` framework reference. A plain SDK project does not.

`DbContextArtifactContributor.Contribute` (`src/Poly.DslCompiler/DbContextArtifactContributor.cs:13-38`) writes the context file from the persistence door. The registration of that context is written by the http door (`MinimalApiHostArtifactContributor.Contribute`, `MinimalApiGenerator.cs:1193-1232`). Wiring and the context type are already different producers.

### 3. Emitted domain surface — no

Entity files, `Poly.Types.cs`, and the generated adapter are container-free. The crm adapter golden is a private constructor and one static method:

```52:56:Poly.Tests/DomainModeling/Compile/EmitGolden/crm/Poly.Types.cs.golden
public class BillingAdapters
{
    private BillingAdapters()
    { }
    public static DomainResult Charge(ChargeRequest request) => DomainResult.Failure("Contract endpoint 'Billing.Charge' has no in-process adapter.");
}
```

`Opportunity.Capture` calls that method and returns on failure (`Opportunity.cs.golden:394-403`). The Shop baseline `Order.Pay` is the same call with one parameter (`baseline/Order.cs`). Subscription registration walks navigations and calls an instance register method (`DomainToCSharpExporter.Notify.cs:19-62`). There is no dispatcher to resolve.

The DSL guide states the current contract: until an in-process adapter is registered, the emitted method returns that failure (`Poly.Mcp/Docs/poly-dsl-guide.md:268-272`). "Registered" is not implemented. `BuildContractAdapterTypeDef` always returns the failure constant (`DomainToCSharpExporter.Actions.cs:471-505`).

### 4. Lowered operation module — no, and a container type here is a defect

`DomainProgramProjection.ToSyntax` appends each bound contract's adapter type to the module (`DomainProgramProjection.cs:105-121`) by calling `BuildContractAdapterTypeDef`. `PrependAdapterInvocation` (`DomainToCSharpExporter.Actions.cs:421-464`) inserts `BillingAdapters.Charge(request)` into the action body, and `BuildFullActionBody` (`:75-87`) is the body the module stores. `AddActionMethod` (`:64-69`) copies the action's own parameters and no others.

The interpreter executes that tree. `ParityTests.Invoke_WhenUnboundContractEndpoint_FailsTheSameWay` asserts simulate and print both surface `Contract endpoint 'Stripe.Charge' has no in-process adapter.` A container type in this method is a type the VM would have to resolve, and a print-only static field the VM does not run is an emitter patch. Both are outside the frozen pipeline (`2026-09-04` §9, `2026-09-05`).

## What the container adds, per option

Parameters already carry a clock, a logger, a client, or an adapter into an operation when some host calls that operation. `IServiceProvider` adds lifetime and a composition root for a component nobody calls with arguments: a hosted loop, a keyed choice made once at startup, lazy optional resolution. The last of those is the locator, and it hides the collaborator from the signature the interpreter binds.

### A. Status quo, documented — this decision

Emitted text difference: none. Golden impact: none.

The host file keeps `AddDbContext` and `CreateScope`. Domain files keep parameters and the fail-closed adapter. `IServiceProvider` buys the host its current scoped context and handler injection. It buys the domain nothing it does not already receive as a `DbContext` parameter on the lambda, or as an action parameter on the method.

### B. Generated `Add{Domain}()` on the http door

Emitted text, host only. `Program.cs` line that is now `builder.Services.AddDbContext<ShopDbContext>(...)` becomes `builder.Services.AddShop()`, and a new `ContributedFile` holds:

```csharp
public static class ShopServiceCollectionExtensions {
    public static IServiceCollection AddShop(this IServiceCollection services) {
        services.AddDbContext<ShopDbContext>(options => options.UseSqlite("Data Source=shop.db"));
        return services;
    }
}
```

Golden impact: `EmitGolden/crm-dslcompiler/Program.cs.golden` (the `AddDbContext` line at `:16` and, if the extension is a separate file, a new golden). Entity goldens, `Poly.Types.cs.golden`, `CrmDbContext.cs.golden`, and `demo.http.golden` stay. The compile oracle already imports `Microsoft.Extensions.DependencyInjection` (`DslCompilerCompileOracleTests.cs:131`) and already references the ASP.NET Core shared framework (`GatherReferences`, `:49-84`). No new package and no new implicit using.

`IServiceProvider` still buys only host composition. Moving `AddDbContext` into an extension does not give an action a clock. Tests that lock the inline call — `MinimalApiGeneratorTests.Setup_HasAddDbContext` (`:69-73`) and `EmitSessionContractTests` (`:202`) — would follow the move. Nothing in the domain surface is deleted.

This is legal later, on the existing `http` contributor, and it is not worth a generator change while it only renames the registration that already exists. The 2026-07-22 note that "DI snippets" bind to a unit / context type name (`2026-07-22-persistence-units-medium-facets-pack-syntax-export.md:200`) is this option, anticipated, not built. The parked Minimal API sketch (`docs/plans/parked/ef-and-api-codegen.md:246`) is the same `AddDbContext` line the generator already emits.

### C. A domain port, registered by the host

This is the in-process adapter the failure string names. It is a domain fact plus a host registration, not a container inside the module.

The spike's hand edit is the text difference (`docs/probes/produced-di/`, baseline versus `slice/`):

```diff
- public DomainResult Pay(ChargeRequest request)
- {
-     var adapterResult = BillingAdapters.Charge(request);
+ public DomainResult Pay(ChargeRequest request, IBillingCharge billing)
+ {
+     var adapterResult = billing.Charge(request);
```

`Poly.Types.cs` gains `interface IBillingCharge` and does not mention a container type. `BillingAdapters.Charge` stays the failure constant. The host, which is the part that already resolves `ShopDbContext`, resolves the port:

```csharp
builder.Services.AddSingleton<IBillingCharge, RecordingCharge>();
var billing = app.Services.GetRequiredService<IBillingCharge>();
var paid = created.Value.Pay(new ChargeRequest { Amount = 2500, Currency = "USD" }, billing);
```

On the real emitted route the same edit is one extra lambda parameter beside `db` (`MinimalApiGenerator.cs:665` and `:702`) and one extra argument to the entity call (`:735`). For crm, that is `Program.cs.golden:912` and the `entity.Capture(dto.request)` call at `:933`.

Generator lines, when a domain fact names the port (not before):

- `AddActionMethod` parameter list (`DomainToCSharpExporter.Actions.cs:64-69`) includes the port. The interpreter binds it like any other parameter.
- `PrependAdapterInvocation` (`:421-464`) invokes that parameter instead of the static adapter.
- `AppendActionEndpointStatements` (`MinimalApiGenerator.cs:665`, `:702`, and `:735`) adds the parameter next to `db` and passes it.
- `GenerateCompilationUnit` does not emit `AddSingleton` of an implementation. The compiler does not have the customer's class. The customer writes that one registration in the host project. The probe's `RecordingCharge` is that class.

Golden impact if this ships: `crm/Opportunity.cs.golden` (`:394-403`), `crm/Poly.Types.cs.golden` (the new interface; the adapter stub goes away for that endpoint), `crm-dslcompiler/Program.cs.golden` (the capture lambda at `:912` and `:933`). Oracle usings and references stay. Tests that require the failure string change for domains that supply the port: `DslCompilerCompileOracleTests.Compile_All_BoundContractDomain_EmitCompilableSolution` (`:210-214`), `ContractBindingExportTests`, `ContractAdapterFailClosedTests`, `ParityTests.Invoke_WhenUnboundContractEndpoint_FailsTheSameWay`, `PipelineTransformationTests`, `CrmDogfoodTests`. Unbound endpoints keep the failure. The oracle's compilation-wide implicit using would not catch a domain file that started naming the container; the file text has to stay free of the namespace.

What would be deleted: the static `BillingAdapters.Charge` body for an endpoint whose module method takes the port. The fail-closed string stays for a bind with no port value. Parameter plumbing on the action grows by that one parameter; it is not deleted.

`IServiceProvider` buys the host the place to construct the implementation and choose its lifetime. The action still receives the interface. A static mutable slot set from the host at startup compiles and is the wrong seam: the VM runs the Syntax body, and a field that exists only in the printed C# makes simulate and print diverge.

This option needs no new `uses` id. The port is a fact in the operation, like `bind`. The registration stays a `ContributedFile` from the `http` library. Naming the port in the DSL does touch the parser, printer, tokenizer, and `Poly.Mcp/Docs/poly-dsl-guide.md` in the same change. That change is not this spike.

C8-clean (`docs/domain-modeling/pipeline-convergence-plan.decisions.md:264-266`) said the printed stub should trace and return success, with a real adapter seam later. The code still returns the failure (`BuildContractAdapterTypeDef`). This spike does not pull C8-clean forward and does not treat a container as that seam.

### D. Domain code takes `IServiceProvider` — rejected

Emitted text, inside the module print:

```csharp
using Microsoft.Extensions.DependencyInjection;

public DomainResult Pay(ChargeRequest request)
{
    var billing = BillingPorts.Services.GetRequiredService<IBillingCharge>();
    var adapterResult = billing.Charge(request);
    // ...
}
```

Golden impact: every action body that resolved a service, plus `Poly.Types.cs.golden` if the static provider lives there. `Opportunity.cs.golden:400` would stop being `BillingAdapters.Charge(request)`. The oracle would still go green, because the implicit using is already compilation-wide and the oracle does not run the ASP.NET analyzers. Under `Microsoft.NET.Sdk.Web` with warnings as errors, a direct `BuildServiceProvider` is ASP0000. The slice hit that diagnostic and switched to `WebApplication.CreateBuilder` / `Build`.

Rejected because:

- The container type lands in the lowered module (`ToSyntax` owns this method). That is the kill condition.
- The interpreter has no `IServiceProvider`. Shipped meaning would depend on a host library the module cannot run. Simulate and print diverge, or simulate grows a second locator.
- Lifetime, keyed lookup, and optional resolution become invisible parameters. The signature the VM binds no longer lists the collaborator.
- The motive is that hosts usually do this. The probe shows the host can resolve one port and pass it in. The action does not need to ask the container.

Nothing is deleted. The parameter stays the way an operation receives a collaborator.

## Where the dependency manifest lives

The artifact set has no `.csproj`. The Shop emit wrote five files and no project. `AssertSolutionCompiles` (`DslCompilerCompileOracleTests.cs:108-156`) compiles those files as one console assembly against three hard-coded sets: trusted platform assemblies, the ASP.NET Core shared framework directory that matches the process, and every DLL in the test output (that is how EF Core arrives; `Poly.Tests` references `Microsoft.EntityFrameworkCore.Sqlite`). The implicit usings at `:119-136` include `Microsoft.Extensions.DependencyInjection`.

The customer host project owns the real manifest. `docs/probes/evolve-2026-10-13/library-host/Demo.csproj` is a hand-written `Microsoft.NET.Sdk.Web` project plus one package, `Microsoft.EntityFrameworkCore.Sqlite`. It does not reference DependencyInjection, and it does not need to: the web SDK's shared framework already contains it. Persistence's package is the one the artifact set cannot see.

A produced type that needs a package outside `Microsoft.AspNetCore.App` and outside the EF package the host already references has nowhere to declare that package. Do not emit a `.csproj` to fill that gap. The host project gains the `PackageReference`. `Microsoft.Extensions.Http` (`IHttpClientFactory`) is the example that would force the question; it is not a shared-framework assembly.

## Seam

Library: the existing `http` library (`HttpLibrary.Id` is `"http"`, `Poly/DomainModeling/Libraries/Http/HttpLibrary.cs:11-26`). `DslCompiler.OpenCompileSession` loads it with `MinimalApiHostArtifactContributor` when the id is `http` (`DslCompiler.cs:288-289`). Persistence / sqlite / sqlserver load `DbContextArtifactContributor` (`:290-295`) and do not own the container.

Catalog id: no new one. A composition extension is another `ContributedFile`. `DomainSession.LowerToCatalog` (`DomainSession.cs:222-233`) already declares that type:

```230:231:Poly/DomainModeling/Compile/DomainSession.cs
catalog.DeclareType(GeneratedTree.Type, mayPointAt: []);
catalog.DeclareType(ContributedFile.Type, mayPointAt: [EntityType, ScaffoldingType, GeneratedTree.Type]);
```

`RegisterTrees` (`:262-264`) declares `ScaffoldingType` pointing at `SourceDomainType` and `EntityType` pointing at `SourceEntityType`. A host file that references the generated program tree and the entity trees fits the edges `ContributedFile` already has. No new `DeclareType`. No new `uses` id. The port in option C, when it exists, is a fact lowered into the module, not a door.

Check against the module exporters: `DomainToCSharpExporter` and `ArtifactCatalog` do not mention a container type. `ArtifactCatalog` stores declared types and references (`ArtifactCatalog.cs:27-36`). Meaning stays in the operation AST.

## Probe

`docs/probes/produced-di/` holds the unmodified Shop emit and the hand-edited slice. The domain file is `shop.poly.txt` so it is not a `*.poly` sample. `VmAnalyzerReportTests` requires every `*.poly` under `docs/probes` to appear on its VM-error list, and `ParityTests.PrintedCSharpCompiles` compiles each of them.

`slice/Probe.csproj` has no `PackageReference`. `dotnet run --project docs/probes/produced-di/slice/Probe.csproj -p:NuGetAudit=false` printed `resolved 2500 USD` and exited 0.

`ShopHost.cs` checks, in order: `BillingAdapters.Charge` still fails closed (`:1-5`); a host with no registration returns null from `GetService<IBillingCharge>()` (`:7-10`); `AddSingleton` plus `GetRequiredService` (`:12-15`) yields an instance; `Order.Pay` with that instance (`:21`) records one call, amount 2500, currency USD (`:24`).

The slice is a hand edit of the print. It is not a lowered tree, not an `ArtifactCatalog` member, and not a product type. Shipping the extra `Pay` parameter without a domain fact would be a consumer-specific lowering change, which is the kill condition for doing it inside the http emitter alone.

## Falsifiable claim

A host `ContributedFile` from the `http` contributor may name `IServiceCollection` and call `GetRequiredService`. A file printed from the lowered module — entity types, `Poly.Types.cs`, adapters, subscription handlers — does not contain `IServiceProvider`, `IServiceCollection`, or `Microsoft.Extensions.DependencyInjection`.

The check: drop the implicit using `Microsoft.Extensions.DependencyInjection` from `AssertSolutionCompiles` (`DslCompilerCompileOracleTests.cs:131`). `Program.cs` fails. `Order.cs`, `Poly.Types.cs`, `{Domain}DbContext.cs`, and `demo.http` do not mention the namespace (Shop baseline, crm goldens). The adapter body in the module remains the constant failure string until a domain fact names a port.

The claim is false if any of these land: a container type in `DomainProgramProjection.ToSyntax`'s output; a lowering flag that adds a DI parameter only for the http emitter; an emitted `.csproj`; a package reference on `Poly.csproj` or `Poly.DslCompiler` taken so generated domain code can resolve services.

## What would make us revisit

1. A domain fact that names a port (a clock, an outbound adapter implementation). Then option C's four generator sites above, in one change with the grammar and the DSL guide. The host project, not the module, constructs the implementation.
2. A process-lifetime loop with no request to pass arguments through: the first background subscription or outbox dispatcher. The host file gains `AddHostedService`. The hosted service calls module methods with explicit arguments. Still no container type in the module. This is the first customer-visible reason to grow option B past a rename of `AddDbContext`.
3. Two persistence units in one host. The 2026-07-22 rule is one registration per context type name. Keyed `AddDbContext` is host text. The trigger is a domain that actually has two units; the compiler does not emit that today.
4. A produced type that needs a NuGet package outside `Microsoft.AspNetCore.App` and outside the EF package the host project already references. The host project gains the `PackageReference`. The artifact set still has no `.csproj`.

## Kill criteria

| Condition | Result |
|-----------|--------|
| `Main` or a host escape in core | Not required. The host file is the existing `http` contributor. |
| Consumer-specific lowering flag | Required only if the http emitter adds a port parameter the DSL did not name. Not part of this decision. |
| Container type in the lowered module | Option D. Rejected. |
| Shipped meaning depends on a package that is not lowerable | Option D. MEDI is not a Syntax operation. |
| Breaks shipped ⊆ lowerable, analyze-before-emit, or one CURRENT | No board change. No generator change. |
| Motive is "hosts usually do it" | That is the whole case for shipping B or D now. Neither ships. |

## Non-goals

- No `ArtifactCatalog` member, no product type, no golden edit, no audit edit, no `docs/plans/poly-eng/board.md` row.
- No `AGENTS.md` edit. The frozen-core and agent-target sections already say the module is the meaning and the host calls it.
- No implementation of C8-clean's trace-and-succeed stub.
