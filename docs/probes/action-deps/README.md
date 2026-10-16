# Action-dependency passing spike

Throwaway probe for [`docs/decisions/2026-10-16-action-dependency-passing.md`](../../decisions/2026-10-16-action-dependency-passing.md). Not a dogfood domain, not an artifact-catalog member, and not compiled by `Poly.slnx`. There is no `*.poly` file here.

The project is `Microsoft.NET.Sdk` and references `Microsoft.EntityFrameworkCore.Sqlite` through the repo's central package list. Each shape uses one Sqlite `Data Source=:memory:` connection.

```bash
dotnet run --project docs/probes/action-deps/Probe.csproj -p:NuGetAudit=false
```

Observed on 2026-10-16: stdout

```
threaded ok
locator ok
activated-handler ok
instance-field ok
ambient ok
save-is-late ok
six shapes observed
```

Exit 0.

`threaded` is `Issue(code, db)` passing that context into `Widget.Create`. The second `"A"` returns the unique-constraint failure. `locator` resolves `LabDbContext` from `IServiceProvider` inside the call and also sees the duplicate, because the scope returns one context. `activated-handler` is a scoped `IssueHandler(LabDbContext)` whose `Issue(string)` still calls `Widget.Create(code, db)`. `instance-field` fails the duplicate only for the child that copied the field. `ambient` uses `AsyncLocal` and throws once the slot is cleared. `save-is-late` skips the helper, inserts two rows, and `SaveChanges` throws `DbUpdateException`.
