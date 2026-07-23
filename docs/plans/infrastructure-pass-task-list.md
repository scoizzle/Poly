# Infrastructure Pass Suite — Executable Task List

**Date:** 2026-07-23
**Derived from:** `docs/plans/infrastructure-concern-analyzer-suite.md`

> Each task is self-contained: it lists exactly what file to edit, what to change, and how to verify.
> Tasks are sequential — do not skip ahead. Run `dotnet build` and `dotnet run --project Poly.Tests`
> after every task. Stop if anything fails.

---

## Task Group 1: Layer 0 — Entity Syntax as Analysis Metadata

### Task 1.1 — Write the golden-file test FIRST

**Why first:** The exporter is ~1500 lines with zero tests. You need a safety net before extracting anything.

**What to do:**
1. Open `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs`
2. Read the `Export(Domain domain, AnalysisResult analysis)` method signature and the `MapDomainTypeRef` helper.
3. Use a known-good domain from the library example test: run `Poly.DslCompiler --mode entities` (no dbms) on `docs/experiments/examples/library-checkout.poly` and capture `_all.cs` output.
4. Create a test in `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs`:

```csharp
[Test]
public async Task Export_LibraryDomain_ProducesExpectedOutput()
{
    // Parse + evolve the library-checkout domain
    var domain = DomainFixture.ParseAndEvolve("library-checkout.poly");
    var analysis = DomainModelAnalyzer.Analyze(domain);
    var exporter = new DomainToCSharpExporter();

    var syntaxNodes = exporter.Export(domain, analysis);
    var csharp = new CSharpGenerator().Generate(syntaxNodes);

    await Assert.That(csharp).IsEqualTo(File.ReadAllText("expected/library_all.cs"));
}
```

5. Capture the current `_all.cs` output and save it as the expected file.
6. Run the test — it passes (you're asserting against current output).

**Files:** `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs`, `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs`
**Verify:** `dotnet run --project Poly.Tests` — exactly one new test, passing.

---

### Task 1.2 — Create `EntitySyntaxMetadata` wrapper record

**What to do:**
1. Create `Poly/DomainModeling/Analysis/EntitySyntaxMetadata.cs`:

```csharp
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Stores entity type Syntax nodes produced by <see cref="EntitySyntaxPass"/>.
/// </summary>
public sealed record EntitySyntaxMetadata(
    IReadOnlyList<TypeDefinitionNode> Types
) : IAnalysisMetadata;
```

**Files:** `Poly/DomainModeling/Analysis/EntitySyntaxMetadata.cs` (new)
**Verify:** `dotnet build` — compiles.

---

### Task 1.3 — Create `DomainProgramProjection.ToSyntax()` (mechanical extraction)

**What to do:**
1. Create `Poly/DomainModeling/Lowering/DomainProgramProjection.cs` (new file).
2. Copy all `private static` helper methods from `DomainToCSharpExporter` into this class (make them `internal static`).
3. Add a public entry point:

```csharp
public static class DomainProgramProjection
{
    public static IReadOnlyList<TypeDefinitionNode> ToSyntax(
        Domain domain, AnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);
        // Copy the body of DomainToCSharpExporter.Export() here
    }
}
```

4. In `DomainToCSharpExporter.Export()`, replace the body with:
   `return DomainProgramProjection.ToSyntax(domain, analysis);`

**Critical:** Move the helper methods one at a time. After each move, run the golden-file test from Task 1.1 to verify `_all.cs` output is byte-identical.

**Files:** `Poly/DomainModeling/Lowering/DomainProgramProjection.cs` (new), `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs`
**Verify:** `dotnet run --project Poly.Tests` — golden-file test still passes.

---

### Task 1.4 — Create `EntitySyntaxPass : INodeAnalyzer`

**What to do:**
1. Open `Poly/DomainModeling/Lowering/DomainProgramProjection.cs`.
2. Change `ToSyntax(Domain domain, AnalysisResult analysis)` to accept `INodeMetadataProvider`:

```csharp
public static IReadOnlyList<TypeDefinitionNode> ToSyntax(
    Domain domain, INodeMetadataProvider metadata)
```

3. Update all internal metadata reads from `analysis.GetMetadata<T>(node)` to `metadata.GetMetadata<T>(node)`. Run the golden-file test after this change to verify byte-identical output.
4. Create `Poly/DomainModeling/Analysis/EntitySyntaxPass.cs`:

```csharp
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class EntitySyntaxPass : INodeAnalyzer
{
    public string PassName => "EntitySyntaxPass";
    public string[] Dependencies => [];

    public void Analyze(AnalysisContext context, Node node)
    {
        if (node is not Domain domain) return;
        // AnalysisContext implements INodeMetadataProvider — passes through directly.
        var types = DomainProgramProjection.ToSyntax(domain, context);
        context.SetMetadata(domain, new EntitySyntaxMetadata(types));
    }
}
```

**Files:** `Poly/DomainModeling/Lowering/DomainProgramProjection.cs`, `Poly/DomainModeling/Analysis/EntitySyntaxPass.cs` (new)
**Verify:** `dotnet build` — compiles. Golden-file test still passes.

---

### Task 1.5 — Register `EntitySyntaxPass` in the domain analysis pipeline

**What to do:**
1. Open `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs`
2. Find `UseDomainModelAnalysisPipeline()` method.
3. Add `builder.AddAnalyzer(new EntitySyntaxPass());` as the **last** pass in the pipeline (after all structural/metadata passes).
4. Verify the static `_analyzer` field still works — `EntitySyntaxPass` has no dependencies, so it inserts fine.

**Files:** `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs`
**Verify:** `dotnet build` — compiles. `dotnet run --project Poly.Tests` — all existing tests pass.

---

### Task 1.6 — Read `EntitySyntaxMetadata` from `AnalysisResult` in DslCompiler

**What to do:**
1. Open `src/Poly.DslCompiler/DslCompiler.cs`
2. Find `GenerateAllFiles()` — look for the line that creates entity type `.cs` files via `DomainToCSharpExporter.Export()`.
3. Replace that code path:

```csharp
// BEFORE:
var exporter = new DomainToCSharpExporter();
var combinedGenerator = new CSharpGenerator();
var combinedCs = combinedGenerator.Generate(exporter.Export(domain, analysis));
files.Add(("_all.cs", combinedCs));

// AFTER:
var entitySyntax = analysis.GetMetadata<EntitySyntaxMetadata>(domain);
var combinedCs = new CSharpGenerator().Generate(entitySyntax.Types);
files.Add(("_all.cs", combinedCs));
```

4. Also update per-entity file generation if it also calls the exporter.

**Files:** `src/Poly.DslCompiler/DslCompiler.cs`
**Verify:** Run `dotnet ./src/Poly.DslCompiler/bin/Release/net10.0/Poly.DslCompiler.dll ./docs/experiments/examples/library-checkout.poly` — `_all.cs` output is byte-identical to before.

---

## Task Group 2: Syntax IR Growth + Generator Conversion

### Task 2.1 — Add `CompilationUnitNode`

**What to do:**
1. Create `Poly/Syntax/Nodes/CompilationUnitNode.cs`:

```csharp
namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a single .cs file: usings, namespace, type definitions, and optional top-level statements.
/// </summary>
public sealed record CompilationUnitNode(
    IReadOnlyList<string> Usings,
    string? Namespace,
    IReadOnlyList<TypeDefinitionNode> Types,
    IReadOnlyList<Node>? TopLevelStatements
) : Node;
```

**Files:** `Poly/Syntax/Nodes/CompilationUnitNode.cs` (new)
**Verify:** `dotnet build` — compiles.

---

### Task 2.2 — Add `AttributeNode`

**What to do:**
1. Create `Poly/Syntax/Nodes/AttributeNode.cs`:

```csharp
namespace Poly.Syntax.Nodes;

public sealed record AttributeNode(string Name, IReadOnlyList<Expression> Arguments) : Node;

public sealed record AttributedNode(Node Inner, IReadOnlyList<AttributeNode> Attributes) : Node;
```

2. Add `Attributes` property to `TypeDefinitionNode`, `MethodDefinitionNode`, `FieldDefinitionNode`, `PropertyDefinitionNode`:

```csharp
public IReadOnlyList<AttributeNode> Attributes { get; init; } = [];
```

**Files:** `Poly/Syntax/Nodes/AttributeNode.cs` (new), `Poly/Syntax/Nodes/TypeDefinitions/TypeDefinitionNode.cs`, `Poly/Syntax/Nodes/TypeDefinitions/MethodDefinitionNode.cs`, `Poly/Syntax/Nodes/TypeDefinitions/FieldDefinitionNode.cs`, `Poly/Syntax/Nodes/TypeDefinitions/PropertyDefinitionNode.cs`
**Verify:** `dotnet build` — compiles. Fix any missing Children/ToString implementations.

---

### Task 2.3 — Add `BaseConstructorInvocationNode`

**What to do:**
1. Create `Poly/Syntax/Nodes/BaseConstructorInvocationNode.cs`:

```csharp
namespace Poly.Syntax.Nodes;

public sealed record BaseConstructorInvocationNode(
    IReadOnlyList<Expression> Arguments
) : Node;
```

2. Add `BaseConstructorInvocation` property to `ConstructorDefinitionNode`:

```csharp
public BaseConstructorInvocationNode? BaseConstructorInvocation { get; init; }
```

**Files:** `Poly/Syntax/Nodes/BaseConstructorInvocationNode.cs` (new), `Poly/Syntax/Nodes/TypeDefinitions/ConstructorDefinitionNode.cs`
**Verify:** `dotnet build` — compiles.

---

### Task 2.4 — Extend `CSharpGenerator` to render new nodes

**What to do:**
1. Open `Poly/Interpretation/CSharp/CSharpGenerator.cs`
2. Add rendering for `CompilationUnitNode`:
   - Emit `#nullable enable`
   - Emit usings
   - Emit namespace wrapping if present
   - Emit top-level statements if present
   - Emit type definitions
3. Add rendering for `AttributeNode`: `[Name(args)]`
4. Add rendering for `AttributedNode`: emit attributes, then inner node
5. Add rendering for `BaseConstructorInvocationNode`: ` : base(args)`
6. Add overload: `string Generate(CompilationUnitNode unit)` — delegates to existing type-def rendering with headers.

**Files:** `Poly/Interpretation/CSharp/CSharpGenerator.cs`
**Verify:** Write a small test in `Poly.Tests/Interpretation/CSharpGeneratorTests.cs`:
   - Create a `CompilationUnitNode` with one `TypeDefinitionNode`
   - Call `Generate(unit)`
   - Assert output contains `#nullable enable`, usings, and the class definition.

---

### Task 2.5 — Convert `DbContextGenerator` to Syntax IR

**What to do:**
1. Open `src/Poly.DslCompiler/DbContextGenerator.cs`
2. Add new method `GenerateCompilationUnit() → CompilationUnitNode`:
   - Build `TypeDefinitionNode` for the DbContext class
   - Set usings to `["Microsoft.EntityFrameworkCore"]`
   - Build `OnModelCreating` as a `MethodDefinitionNode` with a `Block` body
   - For each entity: emit fluent config as `Invoke` nodes chained together
3. Keep the old `Generate() → string` method as-is.
4. Add golden-file test: old `Generate()` output == `new CSharpGenerator().Generate(dbGen.GenerateCompilationUnit())`

**Files:** `src/Poly.DslCompiler/DbContextGenerator.cs`
**Verify:** Golden-file test passes. The DbContextGenerator now has two methods — one returns string (old), one returns CompilationUnitNode (new).

---

### Task 2.6 — Convert `MinimalApiGenerator` to Syntax IR

**What to do:**
1. Open `src/Poly.DslCompiler/MinimalApiGenerator.cs`
2. Add new method `GenerateCompilationUnit(string dbContextName) → CompilationUnitNode`:
   - Build top-level statements for `WebApplication.CreateBuilder`, seed, endpoints
   - Build DTO type definitions inline (can be `TypeDefinitionNode` at the bottom)
3. Add golden-file test: old output == new Syntax output.

**Files:** `src/Poly.DslCompiler/MinimalApiGenerator.cs`
**Verify:** Golden-file test passes.

---

### Task 2.7 — Define `IStorageSyntaxEmitter` interface

**What to do:**
1. Create `Poly/DomainModeling/Lowering/IStorageSyntaxEmitter.cs`:

```csharp
using Poly.Syntax.Nodes;

namespace Poly.DomainModeling.Lowering;

public interface IStorageSyntaxEmitter
{
    CompilationUnitNode EmitDbContext(CompilationUnitNode tree, StorageMappingMetadata storage);

    CompilationUnitNode EmitApi(CompilationUnitNode tree, StorageMappingMetadata storage,
        IReadOnlyList<QueryableEndpoint>? queryable);
}
```

2. Make `QueryableEndpoint` a placeholder type or leave it as `object` for now — it's not implemented yet.

**Files:** `Poly/DomainModeling/Lowering/IStorageSyntaxEmitter.cs` (new)
**Verify:** `dotnet build` — compiles.

---

### Task 2.8 — Wire `IStorageSyntaxEmitter?` into generators

**What to do:**
1. Add `IStorageSyntaxEmitter?` parameter to `DbContextGenerator` constructor:

```csharp
public DbContextGenerator(Domain domain, InfrastructureModel? infraModel = null,
    IStorageSyntaxEmitter? emitter = null)
{
    _infraModel = infraModel ?? new InfrastructureAnalyzer(domain).Analyze();
    _storageLookup = _infraModel.Storage.Entities.ToDictionary(...);
    _emitter = emitter; // stored, not used yet — inert
}
```

2. In `GenerateCompilationUnit()`, after building the tree, if `_emitter` is not null, call `_emitter.EmitDbContext(tree, ...)` — but skip for now (return tree as-is). This is just wiring the seam.

3. Do the same for `MinimalApiGenerator`.

**Files:** `src/Poly.DslCompiler/DbContextGenerator.cs`, `src/Poly.DslCompiler/MinimalApiGenerator.cs`
**Verify:** `dotnet build` — compiles. All golden-file tests pass (emitter is null, no decoration).

---

## Task Group 3: Extract Analysis Passes

### Task 3.1 — Define wrapper metadata records

**What to do:**
1. Create `Poly/DomainModeling/Analysis/EffectTopologyMetadata.cs`:

```csharp
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

public sealed record EffectTopologyMetadata(EffectTopology Topology) : IAnalysisMetadata;
```

2. Repeat for `OwnershipAggregateMetadata`, `BehaviorMetadata`, `StorageMappingMetadata`, `TransportMetadata`, `RestApiMetadata`.

**Files:** `Poly/DomainModeling/Analysis/` (6 new files)
**Verify:** `dotnet build` — compiles.

---

### Task 3.2 — Extract `EffectTopologyPass`

**What to do:**
1. Open `Poly/DomainModeling/Lowering/InfrastructureAnalyzer.cs`
2. Find the `EffectTopologyAnalyzer.Scan(_domain)` call in `Analyze()`.
3. Create `Poly/DomainModeling/Analysis/EffectTopologyPass.cs`:

```csharp
internal sealed class EffectTopologyPass : INodeAnalyzer
{
    public string PassName => "EffectTopologyPass";
    public string[] Dependencies => [];

    public void Analyze(AnalysisContext context, Node node)
    {
        if (node is not Domain domain) return;
        var topology = EffectTopologyAnalyzer.Scan(domain);
        context.SetMetadata(domain, new EffectTopologyMetadata(topology));
    }
}
```

4. Do NOT remove the call from `InfrastructureAnalyzer` yet — it still works with the old path.

**Files:** `Poly/DomainModeling/Analysis/EffectTopologyPass.cs` (new)
**Verify:** `dotnet test --filter EffectTopologyPass` — assertion that pass produces same topology as `EffectTopologyAnalyzer.Scan()`.

---

### Task 3.3 — Extract `OwnershipAggregatePass` and `BehaviorPass`

Same pattern as Task 3.2:

- `OwnershipAggregatePass` — wraps `AggregateAnalyzer`, depends on `EffectTopologyPass`
- `BehaviorPass` — wraps `BehaviorAnalyzer`, no dependency (reads analysis directly)

**Files:** `Poly/DomainModeling/Analysis/OwnershipAggregatePass.cs`, `Poly/DomainModeling/Analysis/BehaviorPass.cs` (new)
**Verify:** Each pass produces metadata matching the current sub-analyzer output.

---

### Task 3.4 — Add `CrossReferencePass`

**What to do:**
1. Create `Poly/DomainModeling/Analysis/CrossReferencePass.cs` — builds directed graph from navigations + subscriptions, detects cycles, produces `EntityDependencyGraphMetadata`.
2. Dependencies: `EffectTopologyPass`, `OwnershipAggregatePass`.

**Files:** `Poly/DomainModeling/Analysis/CrossReferencePass.cs` (new), `Poly/DomainModeling/Analysis/EntityDependencyGraphMetadata.cs` (new)
**Verify:** Unit test with two entities that have mutual navigation — pass detects the cycle and produces a diagnostic.

---

### Task 3.5 — Extract `StoragePass` and `TransportPass`

**What to do:**
1. Create `StoragePass` — wraps `StorageAnalyzer`. Depends on `OwnershipAggregatePass`.
2. Constructor takes `TypeMappingRegistry?` and `IReadOnlyList<IStorageConvention>?` (from `DomainAuthoringContext`) — same params as today's `StorageAnalyzer`.
3. Create `TransportPass` — wraps `TransportAnalyzer`, depends on `OwnershipAggregatePass`.
4. After both pass unit tests, remove the corresponding calls from `InfrastructureAnalyzer.Analyze()`.
5. `InfrastructureAnalyzer` now only exists as a facade that calls the extracted passes.

**Files:** `Poly/DomainModeling/Analysis/StoragePass.cs`, `Poly/DomainModeling/Analysis/TransportPass.cs` (new)
**Verify:** Unit tests assert pass output matches old `InfrastructureAnalyzer` sub-model values. Golden-file generator tests still pass.

---

### Task 3.6 — Add `StorageAccessPass`

**What to do:**
1. Create `Poly/DomainModeling/Analysis/StorageAccessPass.cs` — consumes `StorageMappingMetadata` and produces `StorageAccessMetadata` (query filter shapes, navigation traversal paths, result projections, mutation column sets).
2. Dependencies: `StoragePass`.
3. This is where policy expression trees (e.g. `Price >= min AND Price <= max`) are lowered to generic filter patterns that any target can render into its dialect.
4. Create `Poly/DomainModeling/Analysis/StorageAccessMetadata.cs` — the metadata record.

**Files:** `Poly/DomainModeling/Analysis/StorageAccessPass.cs` (new), `Poly/DomainModeling/Analysis/StorageAccessMetadata.cs` (new)
**Verify:** Unit test with a policy expression — pass produces the correct generic filter pattern.

---

### Task 3.7 — Add `RestApiSurfacePass`

**What to do:**
1. Extract route/DTO computation from `MinimalApiGenerator` and `HttpFileGenerator` into a shared `RestApiSurfacePass`.
2. Pass depends on `StorageAccessPass` + `TransportPass` + `BehaviorPass`.
3. Produces `RestApiMetadata` — routes, DTOs, seed hints, query endpoints.

**Files:** `Poly/DomainModeling/Analysis/RestApiSurfacePass.cs` (new)
**Verify:** Unit test asserts REST metadata matches both generators' computed routes.

---

### Task 3.8 — Wire `PassRegistry` on `DomainAuthoringContext`

**What to do:**
1. Open `Poly/DomainModeling/DomainAuthoringContext.cs`.
2. Add `public PassRegistry Passes { get; } = new();`.
3. Create `Poly/DomainModeling/PassRegistry.cs`:

```csharp
namespace Poly.DomainModeling;

public sealed class PassRegistry
{
    private readonly List<INodeAnalyzer> _passes = new();
    public void AddAnalyzer(INodeAnalyzer pass) => _passes.Add(pass);
    internal IEnumerable<INodeAnalyzer> Build() => _passes;
}
```

4. In `UsePersistencePasses()`, after adding the base passes, enumerate `authoring.Passes.Build()` and add each pack-registered pass.

**Files:** `Poly/DomainModeling/DomainAuthoringContext.cs`, `Poly/DomainModeling/PassRegistry.cs` (new)
**Verify:** `dotnet build` — compiles. Test: register a dummy pass, verify it appears in `UsePersistencePasses()` output.

---

## Task Group 4: Wire Generators to Metadata

### Task 4.1 — Port `DbContextGenerator` to consume metadata

**What to do:**
1. Change `DbContextGenerator` constructor: replace `InfrastructureModel?` with `EntitySyntaxMetadata` + `StorageMappingMetadata?`.
2. Remove `_storageLookup` dictionary — read directly from `StorageMappingMetadata.Storage.Entities`.
3. Remove fallback `new InfrastructureAnalyzer(domain).Analyze()`.

**Files:** `src/Poly.DslCompiler/DbContextGenerator.cs`
**Verify:** Golden-file test: `GenerateCompilationUnit()` output matches pre-migration output byte-for-byte.

---

### Task 4.2 — Port `MinimalApiGenerator` and `HttpFileGenerator`

**What to do:**
1. `MinimalApiGenerator`: replace `InfrastructureModel?` constructor param with `EntitySyntaxMetadata` + `RestApiMetadata` + `StorageMappingMetadata`.
   - Remove `_transportLookup`, `_storageLookup`, `_behaviorLookup`, `_aggregateLookup` dictionaries.
2. `HttpFileGenerator`: replace `InfrastructureModel?` with `RestApiMetadata` + `OwnershipAggregateMetadata`.
   - Remove `_storageLookup`, `_behaviorLookup`, `_aggregateLookup` dictionaries.

**Files:** `src/Poly.DslCompiler/MinimalApiGenerator.cs`, `src/Poly.DslCompiler/HttpFileGenerator.cs`
**Verify:** Golden-file tests pass.

---

### Task 4.3 — Delete `InfrastructureModel` / `InfrastructureAnalyzer`

**What to do:**
1. Search for all references to `InfrastructureModel` and `InfrastructureAnalyzer`.
2. All references are now in generators — already replaced in Tasks 4.1–4.2.
3. Delete the two files.
4. Run full test suite.

**Files:** `Poly/DomainModeling/Lowering/InfrastructureModel.cs`, `Poly/DomainModeling/Lowering/InfrastructureAnalyzer.cs` (deleted)
**Verify:** `dotnet build` succeeds. Full test suite passes.

---

## Task Group 5: DslCompiler Wiring

### Task 5.1 — Wire `AnalyzerBuilder` in DslCompiler

**What to do:**
1. Open `src/Poly.DslCompiler/DslCompiler.cs`
2. In `GenerateAllFiles()`, replace direct pass instantiation with builder:

```csharp
// Domain analysis — always runs, cached
var domainResult = DomainModelAnalyzer.Analyze(domain);

// Entity types — from metadata
var entitySyntax = domainResult.GetMetadata<EntitySyntaxMetadata>(domain);
files.Add(("_all.cs", new CSharpGenerator().Generate(entitySyntax.Types)));

// Infrastructure — runs per unit when storage requested
foreach (var unit in units)
{
    var unitAnalyzer = new AnalyzerBuilder()
        .UseEntityCouplingPasses()
        .UsePersistencePasses(unit.Authoring)
        .UseStorageAccessPasses()
        .UseRestApiPasses()
        .Build();
    var unitResult = unitAnalyzer.Analyze(domain, priorAnalysis: domainResult);

    // DbContext
    var storage = unitResult.GetMetadata<StorageMappingMetadata>(domain)!.Storage;
    var dbGen = new DbContextGenerator(domain, storage);
    files.Add(($"{unit.ContextTypeName}.cs",
        new CSharpGenerator().Generate(dbGen.GenerateCompilationUnit())));

    // API + HTTP
    var restApi = unitResult.GetMetadata<RestApiMetadata>(domain);
    var apiGen = new MinimalApiGenerator(domain, restApi!, storage);
    files.Add(("Program.cs", new CSharpGenerator().Generate(apiGen.GenerateCompilationUnit(unit.ContextTypeName))));

    var httpGen = new HttpFileGenerator(domain, restApi!, storage);
    files.Add(("demo.http", httpGen.Generate()));
}
```

**Files:** `src/Poly.DslCompiler/DslCompiler.cs`
**Verify:** Run DslCompiler with `--mode all --dbms sqlite` — output matches pre-migration output byte-for-byte.

---

## Stopping Point: What's Done

After Task Group 5, the full plan is implemented:

- `InfrastructureAnalyzer` is deleted — all passes live on `AnalyzerBuilder`
- All generators consume typed metadata from `AnalysisResult`
- No generator constructs lookup dicts
- Entity types come from `EntitySyntaxMetadata`
- Pack passes can be registered via `PassRegistry` (Task 3.8)
- `IStorageSyntaxEmitter` seam is wired (inert until packs ship implementations)

`StorageAccessPass` (Task 3.6), `CrossReferencePass` (Task 3.4), and `RestApiSurfacePass` (Task 3.7) are the net-new passes beyond what exists today. Everything else is extraction + wiring.
