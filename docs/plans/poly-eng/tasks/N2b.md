# TASK N2b - Database libraries register their own DbContext artifact
Status: planned. Branch slice/n2b-dbcontext-registers. Lane A. Review 1. Implement mill: Grok (review on OpenCode).
## Scope
N2 (merged `592a137f`, #138) moved the HTTP host contributor into `HttpLibrary`. The follow-up half moves the DbContext contributor the same way. `DslCompiler.OpenCompileSession` still registers it from a post-loop id check (`src/Poly.DslCompiler/DslCompiler.cs:290-291`). Add an optional `IArtifactContributor` to the database libraries and have `Register` call `SessionBuilder.AddArtifactContributor` (`Poly/DomainModeling/Compile/SessionBuilder.cs:52-56`), mirroring `HttpLibrary` (`Poly/DomainModeling/Libraries/Http/HttpLibrary.cs:11-25`). `OpenCompileSession` then loads `persistence`, `sqlite`, `sqlserver` with `new DbContextArtifactContributor()` at Load (`:285-288`), and the post-loop is deleted. Per Scot's decision 19 (2026-10-09): the database library registers its own DbContext artifact, with the database kind read from the loaded libraries, exactly as Http registers `demo.http`. `DbContextArtifactContributor` stays in Compile (`DbContextArtifactContributor.cs:12`); `ProductCatalog` (`src/Poly.Packs.Product/ProductCatalog.cs:12-15`) keeps contributor-less instances so MCP is unchanged.
## Files
- `Poly/DomainModeling/Libraries/Storage/PersistenceEmitLibrary.cs` (add optional `IArtifactContributor?`; `Register` adds it)
- `src/Poly.Packs.Sqlite/SqlitePack.cs` (same)
- `src/Poly.Packs.SqlServer/SqlServerPack.cs` (same)
- `src/Poly.Packs.MySql/MySqlPack.cs` (same; DslCompiler does not reference this project, so only an extra `Load` carries a contributor)
- `src/Poly.DslCompiler/DslCompiler.cs` (load loop `:280-289`: construct db libraries with `new DbContextArtifactContributor()`; delete `:290-291`; add `using Poly.DomainModeling.Libraries.Storage;`, `using Poly.Packs.Sqlite;`, `using Poly.Packs.SqlServer;`)
- Named and left alone: `DbContextArtifactContributor` type and body; `ProductCatalog.cs`; `ExtensionCatalog.Core`; MCP; the HTTP switch arm
- Tests, no edit: `Poly.Tests/DomainModeling/Compile/SliceCProducerLoopCatalogTests.cs`, `EmitSessionContractTests.cs`, `EmitGoldenTests.cs`, `Poly.Tests/DomainModeling/Lowering/DslCompilerSqliteOracleTests.cs`, `DslCompilerArtifactContributorTests.cs`, `SqlServerPackTests.cs`, `MySqlPackTests.cs`, `Poly.Tests/Mcp/McpSqliteHttpHarnessTests.cs`
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `CompileMode.Db`/`All`, `DbmsPack.Generic` (id `persistence`) | post-loop `:290-291` adds `DbContextArtifactContributor` | `PersistenceEmitLibrary.Register` adds the contributor passed at Load | `EmitSessionContractTests.Compile_ModeAll_LanguageOnly_SeedsPersistence_EmitsDbContextNotProgram` (`:257-262`) |
| `DbmsPack.Sqlite` or source `uses sqlite` | post-loop adds the contributor; producer `DbContextArtifactContributor` | `SqliteLibrary.Register` adds it; producer unchanged | `SliceCProducerLoopCatalogTests.SliceC_HttpAndPersistence_HostFilesComeFromProducerLoop` (`:103-127`, `:119`); `DslCompilerSqliteOracleTests` (`:95-110`) |
| Extra `Load(new SqliteLibrary())` | id `sqlite` reaches the post-loop; DbContext emitted | DslCompiler loads `new SqliteLibrary(new DbContextArtifactContributor())` for id `sqlite`, so the caller's contributor-less instance is replaced (same as the HTTP arm) | `DslCompilerSqliteOracleTests.Compile_LoadSqlite_SameSessionMapsLandInDbContext` (`:135-144`) |
| `DbmsPack.SqlServer` / id `sqlserver` | post-loop adds the contributor | `SqlServerLibrary.Register` adds it | `SqlServerPackTests` (library still registers; suite green) |
| `new Compiler().Load(new HttpLibrary())`, no `uses http` | http arm constructs contributor-carrying `HttpLibrary` | unchanged | `EmitSessionContractTests.Compile_LoadHttpLibrary_WithoutUsesHttp_EmitsProgramFile` (`:265-272`) |
| MCP `apply_dsl` of the sellable-shape domain | `ProductCatalog` has contributor-less db libraries | unchanged (no `Program.cs`/`demo.http`) | `McpSqliteHttpHarnessTests.Apply_SellableShape_OpensAnalyzeSimulateAndExport` (`:26-64`) |
| CRM `CrmDbContext.cs` bytes | from the post-loop contributor | from `SqliteLibrary.Register` | `EmitGoldenTests.Crm_DslCompilerHostFiles_MatchGolden` (`:65-82`) |
## Done when
`PersistenceEmitLibrary`, `SqliteLibrary`, `SqlServerLibrary` (and `MySqlLibrary`) `Register` is the `AddArtifactContributor` site for the DbContext artifact. `git grep 'ids.Exists(id => id is "persistence"' -- src/Poly.DslCompiler` is empty and the only product `new DbContextArtifactContributor` is an argument to a database-library construction. The A5a behavioral catalog test (`SliceC_HttpAndPersistence_HostFilesComeFromProducerLoop`) passes with no edit and the DbContext producer is still `nameof(DbContextArtifactContributor)`. `CrmDbContext.cs` golden is byte-identical; full suite green.
## SHIP if / NOT SHIP if
SHIP if the A5a behavioral catalog test needed no change, the post-loop id check is gone, and DbContext bytes/producer are unchanged.

NOT SHIP if that test needed a string edit, if `OpenCompileSession` still `AddArtifactContributor`s DbContext itself, if `DbContextArtifactContributor` moves into Poly, if `ProductCatalog`/MCP grows a contributor, if a new interface/factory appears, or if the HTTP arm is re-touched.
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SliceCProducerLoopCatalogTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitSessionContractTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DslCompilerSqliteOracleTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DslCompilerArtifactContributorTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SqlServerPackTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/MySqlPackTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/McpSqliteHttpHarnessTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
One optional ctor plus one `AddArtifactContributor` line per database library, and one switch arm in the load loop; Scot can open each file cold and change it by hand.
## Needs Scot
none
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-09 | planner | opencode/deepseek-v4.1-flash | 592a137f | planned | - |
