# Produced-code dependency injection spike

Throwaway experiment for [`docs/decisions/2026-10-15-produced-code-dependency-injection.md`](../../decisions/2026-10-15-produced-code-dependency-injection.md). Not a dogfood domain, not an artifact-catalog member, and not compiled by `Poly.slnx`.

The domain source is `shop.poly.txt` on purpose. `VmAnalyzerReportTests` pins every `*.poly` file under `docs/probes`, and `ParityTests.PrintedCSharpCompiles` compiles every one of them. A spike file with the `.poly` extension would change that audit.

## Baseline

Unmodified compiler output. Regenerated with:

```bash
dotnet run --project src/Poly.DslCompiler/Poly.DslCompiler.csproj -p:NuGetAudit=false -- \
  --mode all --http --dbms sqlite \
  docs/probes/produced-di/shop.poly.txt \
  docs/probes/produced-di/baseline
```

That wrote `Order.cs`, `Poly.Types.cs`, `Program.cs`, `ShopDbContext.cs`, and `demo.http`. Only `Program.cs` mentions the container (`AddDbContext`, `CreateScope`, `GetRequiredService`).

## Slice

`slice/` is the baseline domain print plus two hand edits, and a host that resolves one port:

- `Order.Pay` takes an `IBillingCharge` and calls it. The baseline method calls `BillingAdapters.Charge(request)`.
- `Poly.Types.cs` adds the `IBillingCharge` interface. `BillingAdapters.Charge` is unchanged and still returns the fail-closed failure.
- `ShopHost.cs` is the composition root. It is not generated.

`slice/Probe.csproj` is `Microsoft.NET.Sdk.Web` with no `PackageReference`. The ASP.NET Core shared framework supplies `Microsoft.Extensions.DependencyInjection`.

```bash
dotnet run --project docs/probes/produced-di/slice/Probe.csproj -p:NuGetAudit=false
```

Observed on 2026-10-15: stdout `resolved 2500 USD`, exit 0. The empty host's `GetService<IBillingCharge>()` was null. After `AddSingleton<IBillingCharge, RecordingCharge>()`, `GetRequiredService` returned that instance, `Order.Pay` called it once with amount 2500 and currency USD, and `BillingAdapters.Charge` still failed with `no in-process adapter`.

Calling `BuildServiceProvider` directly failed the web SDK analyzer ASP0000 under warnings-as-errors. The slice uses `WebApplication.CreateBuilder` / `Build`, the same pair `Program.cs` already emits.
