using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Parsing;
using Poly.DslCompiler;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class MinimalApiGeneratorTests {
    private static Domain ParseDomain(string poly) {
        var ctx = DomainInputBuilder.CreateWithSqlPack().Build();
        var parser = new PolyDslParser(poly, ctx.Parser);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        if (!result.Succeeded) throw new InvalidOperationException("Domain evolution failed");
        return result.Root!;
    }

    /// <summary>Returns the rendered C# from the IR-backed MinimalApi generator.</summary>
    private static string Render(Domain d) =>
        GenerationAssertions.MinimalApiIr(d).Render();

    /// <summary>Returns the IR CompilationUnitNode for structural assertions.</summary>
    private static CompilationUnitNode IrUnit(Domain d) =>
        GenerationAssertions.MinimalApiIr(d);

    // ── Structural IR assertions (preferred) ─────────────────

    [Test]
    public async Task Endpoints_MapGetAndMapPost() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var unit = IrUnit(d);
        var names = unit.TopLevelInvocationNames();
        await Assert.That(names.Contains("MapGet")).IsTrue();
        await Assert.That(names.Contains("MapPost")).IsTrue();
    }

    [Test]
    public async Task Setup_HasCreateBuilder() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var unit = IrUnit(d);
        var names = unit.TopLevelInvocationNames();
        await Assert.That(names.Contains("CreateBuilder")).IsTrue();
    }

    [Test]
    public async Task Setup_HasAddDbContext() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var unit = IrUnit(d);
        var names = unit.TopLevelInvocationNames();
        await Assert.That(names.Contains("AddDbContext")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_Present() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Activate: action { }\n}");
        var unit = IrUnit(d);
        var rendered = Render(d);
        await Assert.That(rendered.Contains("Activate")).IsTrue();
        await Assert.That(rendered.Contains("MapPost")).IsTrue();
    }

    [Test]
    public async Task Child_RouteHasMapGet() {
        var d = ParseDomain("domain T\nPatron: entity { Name: Text }\nLoan: entity { Amount: Number }");
        var unit = IrUnit(d);
        var names = unit.TopLevelInvocationNames();
        await Assert.That(names.Contains("MapGet")).IsTrue();
    }

    // ── Rendered-string assertions (where structural is overkill) ──

    [Test]
    public async Task Seed_IsPresent() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("SeedAsync")).IsTrue();
    }

    [Test]
    public async Task Detail_UsesBindingName() {
        var d = ParseDomain("domain T\nItem: entity { Sku: Text unique }");
        await Assert.That(Render(d).Contains("is Item item")).IsTrue();
    }

    [Test]
    public async Task Detail_UsesTernary() {
        var d = ParseDomain("domain T\nItem: entity { Sku: Text unique }");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("Results.Ok")).IsTrue();
        await Assert.That(rendered.Contains("Results.NotFound")).IsTrue();
    }

    [Test]
    public async Task List_UsesToListAsync() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("ToListAsync")).IsTrue();
    }

    [Test]
    public async Task Post_HasCreateCall() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains(".Create(")).IsTrue();
    }

    [Test]
    public async Task Post_HasSaveChanges() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("SaveChangesAsync")).IsTrue();
    }

    [Test]
    public async Task Post_HasCreate_NotInSeed() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var parts = Render(d).Split("static async Task SeedAsync");
        await Assert.That(parts.Length).IsGreaterThanOrEqualTo(2);
        await Assert.That(parts[0].Contains(".Create(")).IsTrue();
    }

    [Test]
    public async Task Post_ChecksIsSuccess() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("IsSuccess")).IsTrue();
    }

    [Test]
    public async Task Post_HasConflict() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("Conflict")).IsTrue();
    }

    [Test]
    public async Task Setup_HasUsingScope() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("using (var scope")).IsTrue();
    }

    [Test]
    public async Task Setup_HasUseInMemoryDatabase() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("UseInMemoryDatabase")).IsTrue();
    }

    [Test]
    public async Task Dto_RecordPresent() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("ItemDto")).IsTrue();
    }

    [Test]
    public async Task BadRequest_Present() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("Conflict")).IsTrue();
    }

    [Test]
    public async Task CreatedKeyAccess_Present() {
        var d = ParseDomain("domain T\nItem: entity { Sku: Text unique }");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("Created")).IsTrue();
        await Assert.That(rendered.Contains("Value.Sku")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_HasTryCatch() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("try")).IsTrue();
        await Assert.That(rendered.Contains("catch")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_HasStatusCode() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("StatusCode")).IsTrue();
        await Assert.That(rendered.Contains("500")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_HasIsSuccessBranch() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("IsSuccess")).IsTrue();
        await Assert.That(rendered.Contains("Conflict")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_HasNotFoundMessage() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        await Assert.That(Render(d).Contains("Item not found")).IsTrue();
    }

    [Test]
    public async Task NoCommentStubsInIr() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("/* child")).IsFalse();
        await Assert.That(rendered.Contains("/* action")).IsFalse();
    }

    [Test]
    public async Task Create_MissingEntityStructureMetadata_Throws() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var analysis = DomainModelAnalyzer.Analyze(d);
        var storage = analysis.GetMetadata<StorageMappingMetadata>(d)!.Storage;
        var behavior = analysis.GetMetadata<BehaviorMetadata>(d)!.Behavior;
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(d)!.Aggregate;
        var item = d.Types.OfType<Entity>().First();
        analysis.GetMetadataStore().Remove<EntityStructureMetadata>(item);
        var gen = new MinimalApiGenerator(d, analysis, storage, behavior, aggregate);

        var ex = Assert.Throws<InvalidOperationException>(() => gen.GenerateCompilationUnit("TDbCtx"));
        await Assert.That(ex!.Message).Contains("EntityStructureMetadata is required");
    }

    [Test]
    public async Task ValueSetUnion_PropagatesAllowedValuesToDtos() {
        // Transport: equals(v) value-set constraints propagate as [AllowedValues] — the
        // member must equal the pinned value. On the entity create DTO from the declared
        // constraint; on the action DTO implicitly from `assign Status to value`.
        var d = ParseDomain("""
            domain T
            Item: entity {
              Status: Text
              SetStatus: action (value: Text) {
                assign Status to value
              }
            }
            """);
        // The DSL does not author equals(...) value-set constraints, so inject the
        // model-level EqualityConstraint directly — the transport must still propagate it.
        var item = d.Types.OfType<Entity>().First();
        item = item with {
            Properties = item.Properties.Select(p => p.Name == "Status"
                ? new Property("Status", p.Type, [new EqualityConstraint("Active")])
                : p).ToList()
        };
        var types = d.Types.Select(t => ReferenceEquals(t, d.Types.OfType<Entity>().First()) ? item : t).ToList();
        var rendered = Render(new Domain(d.Name, types));

        await Assert.That(rendered).Contains("[AllowedValues(\"Active\")]\n    public string Status { get; init; }");
        await Assert.That(rendered).Contains("[AllowedValues(\"Active\")]\n    public string value { get; init; }");
    }
}